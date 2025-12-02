// badfs3_tool.c -- Host-side tool to inspect/manipulate BadFS3 images
// Build: gcc -std=c11 -O2 -o badfs3_tool badfs3_tool.c

#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <ctype.h>
#include <errno.h>

#define MAGIC_STR "BADFS3"
#define MAGIC_LEN 6
#define DEFAULT_BLOCKSIZE 512

#define FAT_EOC 0xFFFFFFFFu
#define FAT_FREE 0x00000000u

// helper: little-endian reads/writes
static uint32_t le32(const unsigned char *b) {
    return (uint32_t)b[0] | ((uint32_t)b[1] << 8) | ((uint32_t)b[2] << 16) | ((uint32_t)b[3] << 24);
}
static void write_le32(unsigned char *b, uint32_t v) {
    b[0] = v & 0xFF;
    b[1] = (v >> 8) & 0xFF;
    b[2] = (v >> 16) & 0xFF;
    b[3] = (v >> 24) & 0xFF;
}

// Globals populated from header
static uint32_t DIR_ENTRIES_PER_BLOCK = 8;
static uint32_t FAT_ENTRIES_PER_BLOCK = 128;
static uint32_t DATA_START_LBA = 3;
static uint32_t DIR_ENTRY_SIZE = 64;
static uint32_t NAME_MAX = 31;

// read block at LBA into buf (buf must be blocksize)
static int read_block(FILE *f, uint64_t lba, unsigned char *buf, size_t blocksize) {
    uint64_t off = lba * (uint64_t)blocksize;
    if (fseek(f, (long)off, SEEK_SET) != 0) return -1;
    size_t r = fread(buf, 1, blocksize, f);
    return r == blocksize ? 0 : -1;
}
static int write_block(FILE *f, uint64_t lba, unsigned char *buf, size_t blocksize) {
    uint64_t off = lba * (uint64_t)blocksize;
    if (fseek(f, (long)off, SEEK_SET) != 0) return -1;
    size_t w = fwrite(buf, 1, blocksize, f);
    fflush(f);
    return w == blocksize ? 0 : -1;
}

// read header and populate globals
static int read_header(FILE *f, size_t blocksize) {
    unsigned char *hdr = malloc(blocksize);
    if (!hdr) return -1;
    if (read_block(f, 0, hdr, blocksize) != 0) { free(hdr); return -1; }

    if (memcmp(hdr, MAGIC_STR, MAGIC_LEN) != 0) {
        free(hdr);
        return -2; // not BadFS3
    }

    // In your Format() you wrote bytes at offset 6.. and uints at 8,12,16
    // header[6] and header[7] were version info; we only care about 8/12/16
    uint32_t dcount = le32(hdr + 8);
    uint32_t fcount = le32(hdr + 12);
    uint32_t data_start = le32(hdr + 16);

    if (dcount >= 1 && dcount < 1000) DIR_ENTRIES_PER_BLOCK = dcount;
    if (fcount >= 1 && fcount < 1000000) FAT_ENTRIES_PER_BLOCK = fcount;
    if (data_start >= 1 && data_start < 1000000) DATA_START_LBA = data_start;

    free(hdr);
    return 0;
}

// load single directory block and return allocated buffer
static unsigned char *load_dir(FILE *f, size_t blocksize) {
    unsigned char *b = malloc(blocksize);
    if (!b) return NULL;
    if (read_block(f, 1, b, blocksize) != 0) { free(b); return NULL; }
    return b;
}

// save dir block
static int save_dir(FILE *f, unsigned char *d, size_t blocksize) {
    return write_block(f, 1, d, blocksize);
}

// load FAT (single block) -> returns allocated uint32 array
static uint32_t *load_fat(FILE *f, size_t blocksize) {
    unsigned char *b = malloc(blocksize);
    if (!b) return NULL;
    if (read_block(f, 2, b, blocksize) != 0) { free(b); return NULL; }
    uint32_t *fat = malloc(sizeof(uint32_t) * FAT_ENTRIES_PER_BLOCK);
    if (!fat) { free(b); return NULL; }
    for (uint32_t i = 0; i < FAT_ENTRIES_PER_BLOCK; ++i) fat[i] = le32(b + i * 4);
    free(b);
    return fat;
}
static int save_fat(FILE *f, uint32_t *fat, size_t blocksize) {
    unsigned char *b = calloc(1, blocksize);
    if (!b) return -1;
    for (uint32_t i = 0; i < FAT_ENTRIES_PER_BLOCK && i * 4 + 3 < blocksize; ++i) write_le32(b + i * 4, fat[i]);
    int rc = write_block(f, 2, b, blocksize);
    free(b);
    return rc;
}

// helper read name from dir block entry offset
static char *read_name(unsigned char *d, uint32_t off) {
    // name at offset off, max NAME_MAX
    int len = 0;
    for (int i = 0; i < NAME_MAX; ++i) {
        unsigned char c = d[off + i];
        if (c == 0) break;
        len++;
    }
    if (len == 0) return NULL;
    char *s = malloc(len + 1);
    memcpy(s, d + off, len);
    s[len] = 0;
    return s;
}

// read uint32 from dir block at offset
static uint32_t read_uint32_at(unsigned char *d, uint32_t off) {
    return le32(d + off);
}

// find entry index for name under parent
static int find_under(unsigned char *dirblock, int parent, const char *name) {
    for (uint32_t i = 0; i < DIR_ENTRIES_PER_BLOCK; ++i) {
        uint32_t off = i * DIR_ENTRY_SIZE;
        uint32_t par = read_uint32_at(dirblock, off + 32);
        if ((int)par != parent) continue;
        char *nm = read_name(dirblock, off);
        if (!nm) { if (nm) free(nm); continue; }
        int cmp = strcmp(nm, name);
        free(nm);
        if (cmp == 0) return (int)i;
    }
    return -1;
}

// resolve path to dir index (mirrors your ResolvePath)
static int resolve_path(FILE *f, const char *path, size_t blocksize) {
    if (!path || path[0] != '/') return -1;
    if (strcmp(path, "/") == 0) return 0;

    // trim trailing slash
    char *t = strdup(path);
    size_t L = strlen(t);
    while (L > 1 && t[L-1] == '/') { t[L-1] = 0; L--; }
    // split components
    char *p = t + 1;
    unsigned char *dirblock = load_dir(f, blocksize);
    if (!dirblock) { free(t); return -1; }
    int cur = 0;
    char *token;
    char *saveptr;
    token = strtok_r(p, "/", &saveptr);
    while (token) {
        int found = -1;
        for (uint32_t i = 0; i < DIR_ENTRIES_PER_BLOCK; ++i) {
            uint32_t off = i * DIR_ENTRY_SIZE;
            uint32_t par = read_uint32_at(dirblock, off + 32);
            if ((int)par != cur) continue;
            char *nm = read_name(dirblock, off);
            if (!nm) { if (nm) free(nm); continue; }
            if (strcmp(nm, token) == 0) { found = (int)i; free(nm); break; }
            free(nm);
        }
        if (found < 0) { free(dirblock); free(t); return -1; }
        cur = found;
        token = strtok_r(NULL, "/", &saveptr);
    }
    free(dirblock);
    free(t);
    return cur;
}

// list directory
static int cmd_ls(FILE *f, const char *path, size_t blocksize) {
    int idx = resolve_path(f, path, blocksize);
    if (idx < 0) { fprintf(stderr, "Not found: %s\n", path); return 1; }
    unsigned char *d = load_dir(f, blocksize);
    if (!d) { fprintf(stderr, "Failed to read dir\n"); return 1; }

    for (uint32_t i = 0; i < DIR_ENTRIES_PER_BLOCK; ++i) {
        uint32_t off = i * DIR_ENTRY_SIZE;
        uint32_t par = read_uint32_at(d, off + 32);
        if ((int)par != idx) continue;
        char *nm = read_name(d, off);
        if (!nm) continue;
        int isdir = d[off + 36] == 1;
        if (isdir) printf("<DIR>  %s\n", nm);
        else printf("       %s\n", nm);
        free(nm);
    }
    free(d);
    return 0;
}

// find free dir slot
static int find_free_dir(unsigned char *d) {
    for (uint32_t i = 0; i < DIR_ENTRIES_PER_BLOCK; ++i) {
        int empty = 1;
        for (uint32_t j = 0; j < NAME_MAX; ++j) {
            if (d[i * DIR_ENTRY_SIZE + j] != 0) { empty = 0; break; }
        }
        if (empty) return (int)i;
    }
    return -1;
}

// free chain in fat starting at start
static void free_chain(uint32_t *fat, uint32_t start) {
    uint32_t cur = start;
    while (cur != FAT_EOC) {
        uint32_t nxt = fat[cur];
        fat[cur] = FAT_FREE;
        if (nxt == FAT_EOC) break;
        cur = nxt;
    }
}

// allocate n free blocks from FAT. returns malloc'd array of ints (count) or NULL
static int *alloc_blocks(uint32_t *fat, int count) {
    int *list = malloc(sizeof(int) * count);
    if (!list) return NULL;
    int found = 0;
    for (uint32_t i = 0; i < FAT_ENTRIES_PER_BLOCK && found < count; ++i) {
        if (fat[i] == FAT_FREE) { list[found++] = (int)i; }
    }
    if (found != count) { free(list); return NULL; }
    return list;
}

// write directory entry at index
static void write_dir_entry(unsigned char *d, int idx, const char *name, uint32_t parent, int is_dir, uint32_t start, uint32_t size) {
    uint32_t off = idx * DIR_ENTRY_SIZE;
    for (uint32_t i = 0; i < DIR_ENTRY_SIZE; ++i) d[off + i] = 0;
    size_t n = strlen(name);
    if (n > NAME_MAX) n = NAME_MAX;
    memcpy(d + off, name, n);
    write_le32(d + off + 32, parent);
    d[off + 36] = is_dir ? 1 : 0;
    write_le32(d + off + 37, start);
    write_le32(d + off + 41, size);
}

// cmd_cat /path -> print to stdout
static int cmd_cat(FILE *f, const char *path, size_t blocksize) {
    int idx = resolve_path(f, path, blocksize);
    if (idx < 0) { fprintf(stderr, "Not found: %s\n", path); return 1; }
    unsigned char *d = load_dir(f, blocksize);
    if (!d) return 1;
    uint32_t off = idx * DIR_ENTRY_SIZE;
    if (d[off + 36] == 1) { fprintf(stderr, "Is a directory\n"); free(d); return 1; }
    uint32_t start = read_uint32_at(d, off + 37);
    uint32_t size = read_uint32_at(d, off + 41);
    free(d);

    uint32_t *fat = load_fat(f, blocksize);
    if (!fat) { fprintf(stderr, "Failed to load FAT\n"); return 1; }

    uint32_t cur = start;
    uint32_t pos = 0;
    unsigned char *blk = malloc(blocksize);
    while (cur != FAT_EOC && pos < size) {
        if (read_block(f, DATA_START_LBA + cur, blk, blocksize) != 0) { fprintf(stderr, "Read failed\n"); free(blk); free(fat); return 1; }
        uint32_t copy = (size - pos) > blocksize ? blocksize : (size - pos);
        fwrite(blk, 1, copy, stdout);
        pos += copy;
        cur = fat[cur];
    }
    free(blk);
    free(fat);
    return 0;
}

// cmd_extract
static int cmd_extract(FILE *f, const char *path, const char *outfile, size_t blocksize) {
    FILE *out = fopen(outfile, "wb");
    if (!out) { fprintf(stderr, "Failed to open %s: %s\n", outfile, strerror(errno)); return 1; }
    // reuse cat but write to file: implement simplified
    int idx = resolve_path(f, path, blocksize);
    if (idx < 0) { fprintf(stderr, "Not found: %s\n", path); fclose(out); return 1; }
    unsigned char *d = load_dir(f, blocksize);
    if (!d) { fclose(out); return 1; }
    uint32_t off = idx * DIR_ENTRY_SIZE;
    if (d[off + 36] == 1) { fprintf(stderr, "Is a directory\n"); free(d); fclose(out); return 1; }
    uint32_t start = read_uint32_at(d, off + 37);
    uint32_t size = read_uint32_at(d, off + 41);
    free(d);

    uint32_t *fat = load_fat(f, blocksize);
    if (!fat) { fclose(out); return 1; }

    uint32_t cur = start;
    uint32_t pos = 0;
    unsigned char *blk = malloc(blocksize);
    while (cur != FAT_EOC && pos < size) {
        if (read_block(f, DATA_START_LBA + cur, blk, blocksize) != 0) { fprintf(stderr, "Read failed\n"); free(blk); free(fat); fclose(out); return 1; }
        uint32_t copy = (size - pos) > blocksize ? blocksize : (size - pos);
        fwrite(blk, 1, copy, out);
        pos += copy;
        cur = fat[cur];
    }
    free(blk);
    free(fat);
    fclose(out);
    return 0;
}

// cmd_rm
static int cmd_rm(FILE *f, const char *path, size_t blocksize) {
    int idx = resolve_path(f, path, blocksize);
    if (idx <= 0) { fprintf(stderr, "Not found or cannot delete root: %s\n", path); return 1; }
    unsigned char *d = load_dir(f, blocksize);
    if (!d) return 1;
    uint32_t off = idx * DIR_ENTRY_SIZE;
    int isdir = d[off + 36] == 1;
    if (isdir) {
        // check empty
        for (uint32_t i = 0; i < DIR_ENTRIES_PER_BLOCK; ++i) {
            if (read_uint32_at(d, i * DIR_ENTRY_SIZE + 32) == (uint32_t)idx) {
                fprintf(stderr, "Directory not empty\n");
                free(d);
                return 1;
            }
        }
        // clear entry
        for (uint32_t i = 0; i < DIR_ENTRY_SIZE; ++i) d[off + i] = 0;
        int ok = save_dir(f, d, blocksize);
        free(d);
        return ok == 0 ? 0 : 1;
    } else {
        uint32_t start = read_uint32_at(d, off + 37);
        free(d);
        uint32_t *fat = load_fat(f, blocksize);
        if (!fat) return 1;
        free_chain(fat, start);
        if (save_fat(f, fat, blocksize) != 0) { free(fat); return 1; }
        free(fat);
        // clear dir entry
        unsigned char *dirb = load_dir(f, blocksize);
        if (!dirb) return 1;
        for (uint32_t i = 0; i < DIR_ENTRY_SIZE; ++i) dirb[off + i] = 0;
        int ok = save_dir(f, dirb, blocksize);
        free(dirb);
        return ok == 0 ? 0 : 1;
    }
}

// cmd_write: write host infile into image at /path (create or replace)
// NOTE: for simplicity supports only parent directories that already exist
static int cmd_write(FILE *f, const char *path, const char *infile, size_t blocksize) {
    // resolve parent and name
    if (!path || path[0] != '/') { fprintf(stderr, "Path must be absolute\n"); return 1; }
    // special-case root filename like /name
    char *pcopy = strdup(path);
    size_t L = strlen(pcopy);
    while (L > 1 && pcopy[L-1] == '/') { pcopy[L-1] = 0; L--; }
    char *slash = strrchr(pcopy, '/');
    if (!slash) { free(pcopy); return 1; }
    char *name = slash + 1;
    if (strlen(name) == 0) { free(pcopy); fprintf(stderr, "Invalid name\n"); return 1; }
    // parent path
    if (slash == pcopy) {
        // parent is root
        // parent index = 0
    }
    char parentpath[512];
    if (slash == pcopy) strcpy(parentpath, "/");
    else { strncpy(parentpath, pcopy, slash - pcopy); parentpath[slash - pcopy] = 0; }
    int parentidx = resolve_path(f, parentpath, blocksize);
    if (parentidx < 0) { free(pcopy); fprintf(stderr, "Parent not found: %s\n", parentpath); return 1; }

    // read infile
    FILE *in = fopen(infile, "rb");
    if (!in) { free(pcopy); fprintf(stderr, "Failed to open %s: %s\n", infile, strerror(errno)); return 1; }
    if (fseek(in, 0, SEEK_END) != 0) { fclose(in); free(pcopy); return 1; }
    long sz = ftell(in);
    fseek(in, 0, SEEK_SET);
    if (sz < 0) { fclose(in); free(pcopy); return 1; }
    unsigned char *buf = malloc((size_t)sz);
    if (!buf) { fclose(in); free(pcopy); return 1; }
    if (fread(buf, 1, sz, in) != (size_t)sz) { fclose(in); free(pcopy); free(buf); return 1; }
    fclose(in);

    unsigned char *dirb = load_dir(f, blocksize);
    if (!dirb) { free(pcopy); free(buf); return 1; }

    int existing = find_under(dirb, parentidx, name);
    if (existing >= 0 && dirb[existing * DIR_ENTRY_SIZE + 36] == 1) {
        fprintf(stderr, "Is a directory\n");
        free(pcopy); free(buf); free(dirb); return 1;
    }

    uint32_t *fat = load_fat(f, blocksize);
    if (!fat) { free(pcopy); free(buf); free(dirb); return 1; }

    if (existing >= 0) {
        uint32_t start = read_uint32_at(dirb, existing * DIR_ENTRY_SIZE + 37);
        if (start != 0) free_chain(fat, start);
        // clear dir entry
        for (uint32_t i = 0; i < DIR_ENTRY_SIZE; ++i) dirb[existing * DIR_ENTRY_SIZE + i] = 0;
    }

    int bs = (int)blocksize;
    int need = (int)((sz + bs - 1) / bs);
    if (need == 0) need = 1;
    int *chain = alloc_blocks(fat, need);
    if (!chain) { fprintf(stderr, "Not enough space\n"); free(pcopy); free(buf); free(dirb); free(fat); return 1; }

    // write blocks
    int pos = 0;
    for (int i = 0; i < need; ++i) {
        unsigned char *blk = calloc(1, blocksize);
        int len = (int)((sz - pos) > bs ? bs : (sz - pos));
        if (len > 0) memcpy(blk, buf + pos, len);
        pos += len;
        uint64_t lba = DATA_START_LBA + (uint64_t)chain[i];
        if (write_block(f, lba, blk, blocksize) != 0) { fprintf(stderr, "Write block failed\n"); free(blk); free(pcopy); free(buf); free(dirb); free(fat); free(chain); return 1; }
        free(blk);
    }
    // set fat entries
    for (int i = 0; i < need; ++i) {
        fat[chain[i]] = (i == need - 1) ? FAT_EOC : (uint32_t)chain[i+1];
    }
    if (save_fat(f, fat, blocksize) != 0) { fprintf(stderr, "Failed to save FAT\n"); free(pcopy); free(buf); free(dirb); free(fat); free(chain); return 1; }

    int idx = existing >= 0 ? existing : find_free_dir(dirb);
    if (idx < 0) { fprintf(stderr, "Directory full\n"); free(pcopy); free(buf); free(dirb); free(fat); free(chain); return 1; }

    write_dir_entry(dirb, idx, name, (uint32_t)parentidx, 0, (uint32_t)chain[0], (uint32_t)sz);
    if (save_dir(f, dirb, blocksize) != 0) { fprintf(stderr, "Failed to save dir\n"); free(pcopy); free(buf); free(dirb); free(fat); free(chain); return 1; }

    printf("Wrote %s -> %s (size %ld, blocks %d, start %d)\n", infile, path, sz, need, chain[0]);

    free(pcopy);
    free(buf);
    free(dirb);
    free(fat);
    free(chain);
    return 0;
}

static void print_usage(const char *exe) {
    fprintf(stderr,
        "Usage: %s -i image [-b blocksize] <command> [args]\n"
        "Commands:\n"
        "  info\n"
        "  ls [/path]\n"
        "  cat /path\n"
        "  extract /path outfile\n"
        "  write /path infile\n"
        "  rm /path\n"
        "  help\n", exe);
}

int main(int argc, char **argv) {
    const char *img = NULL;
    size_t blocksize = DEFAULT_BLOCKSIZE;
    int argi = 1;
    while (argi < argc && argv[argi][0] == '-') {
        if (strcmp(argv[argi], "-i") == 0) { argi++; if (argi >= argc) { print_usage(argv[0]); return 1; } img = argv[argi++]; }
        else if (strcmp(argv[argi], "-b") == 0) { argi++; if (argi >= argc) { print_usage(argv[0]); return 1; } blocksize = (size_t)atoi(argv[argi++]); }
        else { print_usage(argv[0]); return 1; }
    }
    if (!img) { print_usage(argv[0]); return 1; }
    if (argi >= argc) { print_usage(argv[0]); return 1; }

    const char *cmd = argv[argi++];

    FILE *f = fopen(img, "rb+");
    if (!f) { fprintf(stderr, "Failed to open %s: %s\n", img, strerror(errno)); return 1; }

    int rh = read_header(f, blocksize);
    if (rh == -2) { fprintf(stderr, "Not a BadFS3 image (magic mismatch)\n"); fclose(f); return 1; }
    if (rh != 0) { fprintf(stderr, "Failed to read header\n"); fclose(f); return 1; }

    if (strcmp(cmd, "info") == 0) {
        printf("BadFS3 image: %s\n", img);
        printf("Blocksize: %zu\n", blocksize);
        printf("DIR_ENTRIES_PER_BLOCK: %u\n", DIR_ENTRIES_PER_BLOCK);
        printf("FAT_ENTRIES_PER_BLOCK: %u\n", FAT_ENTRIES_PER_BLOCK);
        printf("DATA_START_LBA: %u\n", DATA_START_LBA);
        fclose(f);
        return 0;
    } else if (strcmp(cmd, "ls") == 0) {
        const char *path = argi < argc ? argv[argi] : "/";
        int rc = cmd_ls(f, path, blocksize);
        fclose(f);
        return rc;
    } else if (strcmp(cmd, "cat") == 0) {
        if (argi >= argc) { fprintf(stderr, "cat requires path\n"); fclose(f); return 1; }
        int rc = cmd_cat(f, argv[argi], blocksize);
        fclose(f);
        return rc;
    } else if (strcmp(cmd, "extract") == 0) {
        if (argi + 1 >= argc) { fprintf(stderr, "extract requires source and dest\n"); fclose(f); return 1; }
        int rc = cmd_extract(f, argv[argi], argv[argi+1], blocksize);
        fclose(f);
        return rc;
    } else if (strcmp(cmd, "write") == 0) {
        if (argi + 1 >= argc) { fprintf(stderr, "write requires dest and infile\n"); fclose(f); return 1; }
        int rc = cmd_write(f, argv[argi], argv[argi+1], blocksize);
        fclose(f);
        return rc;
    } else if (strcmp(cmd, "rm") == 0) {
        if (argi >= argc) { fprintf(stderr, "rm requires path\n"); fclose(f); return 1; }
        int rc = cmd_rm(f, argv[argi], blocksize);
        fclose(f);
        return rc;
    } else if (strcmp(cmd, "help") == 0) {
        print_usage(argv[0]);
        fclose(f);
        return 0;
    } else {
        fprintf(stderr, "Unknown command: %s\n", cmd);
        print_usage(argv[0]);
        fclose(f);
        return 1;
    }
}

/* Save as bxcc.c and compile:
 *   gcc -std=c11 -O2 -o bxcc bxcc.c
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <ctype.h>

#include <stdbool.h> // For bool type
#include <string.h>  // For strlen

// Function to check if a string starts with a given prefix
bool startsWith(const char* str, const char* prefix) {
    // Get the lengths of the string and the prefix
    size_t str_len = strlen(str);
    size_t prefix_len = strlen(prefix);

    // If the prefix is longer than the string, it cannot be a prefix
    if (prefix_len > str_len) {
        return false;
    }

    // Compare characters from the beginning of both strings
    // up to the length of the prefix
    for (size_t i = 0; i < prefix_len; ++i) {
        if (str[i] != prefix[i]) {
            return false; // Mismatch found
        }
    }

    // If all characters match, the string starts with the prefix
    return true;
}/* Opcodes */
#define OPCODE_PUTCHAR   0x01
#define OPCODE_PRINT     0x02
#define OPCODE_EXIT      0xFF

/* Syscall / console opcodes */
#define OPCODE_READFILE   0x10
#define OPCODE_WRITEFILE  0x11

#define OPCODE_READKEY    0x20
#define OPCODE_CLEAR      0x30
#define OPCODE_SETCURSOR  0x31

#define OPCODE_PRINTBUF   0x12
#define OPCODE_WRITEBUF   0x13
#define OPCODE_PRINTLAST  0x14

/* Jump opcodes for control flow */
#define OPCODE_JMP_REL16  0x50
#define OPCODE_JEQ_LAST   0x51

/* NOOP */
#define OPCODE_NOOP 0xF4

/* header flags */
#define FLAG_FILE_IO   0x01
#define FLAG_CONSOLE   0x02

/* Utility: read whole file */
static char *read_file(const char *path, size_t *out_len) {
    FILE *f = fopen(path, "rb");
    if (!f) return NULL;
    if (fseek(f, 0, SEEK_END) != 0) { fclose(f); return NULL; }
    long sz = ftell(f);
    if (sz < 0) sz = 0;
    fseek(f, 0, SEEK_SET);
    char *buf = malloc((size_t)sz + 1);
    if (!buf) { fclose(f); return NULL; }
    if (sz > 0 && fread(buf, 1, (size_t)sz, f) != (size_t)sz) { free(buf); fclose(f); return NULL; }
    buf[sz] = '\0';
    if (out_len) *out_len = (size_t)sz;
    fclose(f);
    return buf;
}

/* write 32-bit little-endian */
static void write_u32_le(FILE *f, uint32_t v) {
    unsigned char b[4];
    b[0] = v & 0xFF;
    b[1] = (v >> 8) & 0xFF;
    b[2] = (v >> 16) & 0xFF;
    b[3] = (v >> 24) & 0xFF;
    fwrite(b,1,4,f);
}

/* whitespace and // comments */
static char *skip_ws_and_comments(char *p) {
    while (*p) {
        if (*p == ' ' || *p == '\t' || *p == '\r' || *p == '\n') { p++; continue; }
        if (p[0] == '/' && p[1] == '/') {
            p += 2;
            while (*p && *p != '\n') p++;
            continue;
        }
        break;
    }
    return p;
}

/* parse identifier into buf, return new pointer */
static char* parse_ident(char *p, char *buf, size_t bufsize) {
    p = skip_ws_and_comments(p);
    size_t i = 0;
    while (*p && (isalpha((unsigned char)*p) || *p == '_' || isdigit((unsigned char)*p))) {
        if (i + 1 < bufsize) buf[i++] = *p;
        p++;
    }
    buf[i] = '\0';
    return p;
}

/* parse double-quoted string with simple escapes. returns pointer after closing quote.
 *   fills *out with malloc'd buffer and *out_len with its length (NOT null-terminated). */
static char* parse_string_literal(char *p, unsigned char **out, size_t *out_len) {
    p = skip_ws_and_comments(p);
    if (*p != '"') return NULL;
    p++;
    size_t cap = 64, len = 0;
    unsigned char *buf = malloc(cap);
    if (!buf) return NULL;

    while (*p && *p != '"') {
        unsigned char ch;
        if (*p == '\\') {
            p++;
            if (*p == 'n') ch = '\n';
            else if (*p == 't') ch = '\t';
            else if (*p == 'r') ch = '\r';
            else if (*p == '\\') ch = '\\';
            else if (*p == '"') ch = '"';
            else if (*p == '0') ch = '\0';
            else ch = (unsigned char)*p;
            if (*p) p++;
        } else {
            ch = (unsigned char)*p;
            p++;
        }
        if (len + 1 > cap) {
            cap *= 2;
            unsigned char *n = realloc(buf, cap);
            if (!n) { free(buf); return NULL; }
            buf = n;
        }
        buf[len++] = ch;
    }

    if (*p != '"') { free(buf); return NULL; } /* unterminated */
        p++; /* skip closing quote */
        *out = buf;
        *out_len = len;
        return p;
}

/* parse single-quoted char like 'A' or '\n' */
static char* parse_char_literal(char *p, unsigned char *out_ch) {
    p = skip_ws_and_comments(p);
    if (*p != '\'') return NULL;
    p++;
    if (!*p) return NULL;
    unsigned char ch;
    if (*p == '\\') {
        p++;
        if (*p == 'n') ch = '\n';
        else if (*p == 't') ch = '\t';
        else if (*p == 'r') ch = '\r';
        else if (*p == '\\') ch = '\\';
        else if (*p == '\'') ch = '\'';
        else if (*p == '0') ch = '\0';
        else ch = (unsigned char)*p;
        if (*p) p++;
    } else {
        ch = (unsigned char)*p;
        p++;
    }
    if (*p != '\'') return NULL;
    p++;
    *out_ch = ch;
    return p;
}

/* parse integer literal (decimal) */
static char* parse_int_literal(char *p, int *out_val) {
    p = skip_ws_and_comments(p);
    if (!*p) return NULL;
    int sign = 1;
    if (*p == '-') { sign = -1; p++; }
    if (!isdigit((unsigned char)*p)) return NULL;
    long val = 0;
    while (isdigit((unsigned char)*p)) {
        val = val * 10 + (*p - '0');
        p++;
    }
    *out_val = (int)(val * sign);
    return p;
}

/* simple growable byte buffer */
typedef struct {
    unsigned char *data;
    size_t len;
    size_t cap;
} Buffer;

static void buf_init(Buffer *b) { b->data = NULL; b->len = b->cap = 0; }
static void buf_free(Buffer *b) { free(b->data); b->data = NULL; b->len = b->cap = 0; }
static int buf_push(Buffer *b, unsigned char x) {
    if (b->len + 1 > b->cap) {
        size_t ncap = b->cap ? b->cap * 2 : 256;
        unsigned char *n = realloc(b->data, ncap);
        if (!n) return 0;
        b->data = n; b->cap = ncap;
    }
    b->data[b->len++] = x;
    return 1;
}
static int buf_push_bytes(Buffer *b, const unsigned char *src, size_t n) {
    for (size_t i = 0; i < n; ++i) if (!buf_push(b, src[i])) return 0;
    return 1;
}

/* compile a fragment of source (used for loop bodies).
 *   This compiles until a '}' closing brace and returns updated pointer.
 *   It supports all statements except top-level 'loop' (no nested loop support here).
 *   breakiflast <num>; will emit a JEQ placeholder and record its offset via break_positions list.
 */
typedef struct { int *pos; int count; int cap; } IntList;
static void ilist_init(IntList *l) { l->pos = NULL; l->count = l->cap = 0; }
static void ilist_free(IntList *l) { free(l->pos); l->pos = NULL; l->count = l->cap = 0; }
static int ilist_push(IntList *l, int v) {
    if (l->count + 1 > l->cap) {
        int ncap = l->cap ? l->cap * 2 : 16;
        int *n = realloc(l->pos, ncap * sizeof(int));
        if (!n) return 0;
        l->pos = n; l->cap = ncap;
    }
    l->pos[l->count++] = v;
    return 1;
}

/* --- Variable table support --- */
typedef enum { VAR_INT = 1, VAR_STR = 2 } var_type_t;
typedef struct {
    char name[64];
    var_type_t type;
    int int_value;
    char *str_value; /* null-terminated, malloc'd */
} bx_var_t;
#define MAX_VARS 512

static bx_var_t* find_var(bx_var_t *vars, int var_count, const char *name) {
    for (int i = 0; i < var_count; ++i) {
        if (strcmp(vars[i].name, name) == 0) return &vars[i];
    }
    return NULL;
}

/* parse either a string literal or a variable name (string variable).
 * On success returns new p and sets *out to malloc'd buffer and *out_len.
 * If a variable is used it copies its value. */
static char* parse_string_or_var(char *p, unsigned char **out, size_t *out_len, bx_var_t *vars, int var_count, char *errbuf, size_t errlen) {
    p = skip_ws_and_comments(p);
    if (*p == '\"') {
        return parse_string_literal(p, out, out_len);
    } else {
        char name[64];
        char *np = parse_ident(p, name, sizeof(name));
        if (name[0] == '\0') { snprintf(errbuf, errlen, "Expected string literal or variable"); return NULL; }
        bx_var_t *v = find_var(vars, var_count, name);
        if (!v) { snprintf(errbuf, errlen, "Undefined variable: %s", name); return NULL; }
        if (v->type != VAR_STR) { snprintf(errbuf, errlen, "Variable %s is not a string", name); return NULL; }
        size_t sl = strlen(v->str_value);
        unsigned char *buf = malloc(sl);
        if (!buf) { snprintf(errbuf, errlen, "OOM"); return NULL; }
        memcpy(buf, v->str_value, sl);
        *out = buf;
        *out_len = sl;
        return np;
    }
}

/* parse either an integer literal or an integer/char variable */
static char* parse_int_or_var(char *p, int *out_val, bx_var_t *vars, int var_count, char *errbuf, size_t errlen) {
    p = skip_ws_and_comments(p);
    if (*p == '-' || isdigit((unsigned char)*p)) {
        return parse_int_literal(p, out_val);
    } else {
        char name[64];
        char *np = parse_ident(p, name, sizeof(name));
        if (name[0] == '\0') { snprintf(errbuf, errlen, "Expected int literal or variable"); return NULL; }
        bx_var_t *v = find_var(vars, var_count, name);
        if (!v) { snprintf(errbuf, errlen, "Undefined variable: %s", name); return NULL; }
        if (v->type != VAR_INT) { snprintf(errbuf, errlen, "Variable %s is not an int", name); return NULL; }
        *out_val = v->int_value;
        return np;
    }
}

/* compile_fragment: compile code from *p (pointer into source) until '}' is found.
 *   Returns pointer after '}' (or NULL on error). */
static char *compile_fragment(char *p, Buffer *out, char *errbuf, size_t errlen, unsigned char *flags, IntList *break_positions, bx_var_t *vars, int var_count) {
    while (1) {
        p = skip_ws_and_comments(p);
        if (!*p) { snprintf(errbuf, errlen, "Unexpected EOF in fragment"); return NULL; }
        if (*p == '}') { return p + 1; } /* consume '}' and return */

            char ident[64];
            p = parse_ident(p, ident, sizeof(ident));
            if (ident[0] == '\0') { snprintf(errbuf, errlen, "Expected identifier in fragment at: %.20s", p); return NULL; }

            if (strcmp(ident, "print") == 0) {
                unsigned char *str = NULL; size_t slen = 0;
                p = parse_string_or_var(p, &str, &slen, vars, var_count, errbuf, errlen);
                if (!p) { snprintf(errbuf, errlen, "Invalid string literal or variable"); return NULL; }
                p = skip_ws_and_comments(p);
                if (*p != ';') { free(str); snprintf(errbuf, errlen, "Expected ';' after print"); return NULL; }
                p++;
                if (!buf_push(out, OPCODE_PRINT)) { free(str); snprintf(errbuf, errlen, "OOM"); return NULL; }
                if (!buf_push_bytes(out, str, slen)) { free(str); snprintf(errbuf, errlen, "OOM"); return NULL; }
                if (!buf_push(out, 0x00)) { free(str); snprintf(errbuf, errlen, "OOM"); return NULL; }
                free(str);
                continue;
            } else if (strcmp(ident, "putchar") == 0) {
                unsigned char ch;
                p = parse_char_literal(p, &ch);
                if (!p) { snprintf(errbuf, errlen, "Invalid char literal"); return NULL; }
                p = skip_ws_and_comments(p);
                if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after putchar"); return NULL; }
                p++;
                if (!buf_push(out, OPCODE_PUTCHAR)) { snprintf(errbuf, errlen, "OOM"); return NULL; }
                if (!buf_push(out, ch)) { snprintf(errbuf, errlen, "OOM"); return NULL; }
                continue;
            } else if (strcmp(ident, "exit") == 0) {
                p = skip_ws_and_comments(p);
                if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after exit"); return NULL; }
                p++;
                if (!buf_push(out, OPCODE_EXIT)) { snprintf(errbuf, errlen, "OOM"); return NULL; }
                continue;
            } else if (strcmp(ident, "clear") == 0) {
                p = skip_ws_and_comments(p);
                if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after clear"); return NULL; }
                p++;
                *flags |= FLAG_CONSOLE;
                if (!buf_push(out, OPCODE_CLEAR)) { snprintf(errbuf, errlen, "OOM"); return NULL; }
                continue;
            } else if (strcmp(ident, "setcursor") == 0) {
                int x=0,y=0;
                p = parse_int_or_var(p, &x, vars, var_count, errbuf, errlen); if (!p) { snprintf(errbuf, errlen, "Invalid X in setcursor"); return NULL; }
                p = skip_ws_and_comments(p); if (*p == ',') p++;
                p = parse_int_or_var(p, &y, vars, var_count, errbuf, errlen); if (!p) { snprintf(errbuf, errlen, "Invalid Y in setcursor"); return NULL; }
                p = skip_ws_and_comments(p); if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after setcursor"); return NULL; }
                p++;
                *flags |= FLAG_CONSOLE;
                if (!buf_push(out, OPCODE_SETCURSOR)) { snprintf(errbuf, errlen, "OOM"); return NULL; }
                if (!buf_push(out, (unsigned char)(x<0?0:(x>255?255:x)))) { snprintf(errbuf, errlen, "OOM"); return NULL; }
                if (!buf_push(out, (unsigned char)(y<0?0:(y>255?255:y)))) { snprintf(errbuf, errlen, "OOM"); return NULL; }
                continue;
            } else if (strcmp(ident, "readkey") == 0) {
                p = skip_ws_and_comments(p);
                if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after readkey"); return NULL; }
                p++;
                *flags |= FLAG_CONSOLE;
                if (!buf_push(out, OPCODE_READKEY)) { snprintf(errbuf, errlen, "OOM"); return NULL; }
                continue;
            } else if (strcmp(ident, "readfile") == 0) {
                unsigned char *path = NULL; size_t plen = 0;
                p = parse_string_or_var(p, &path, &plen, vars, var_count, errbuf, errlen);
                if (!p) { snprintf(errbuf, errlen, "Invalid string for readfile"); return NULL; }
                p = skip_ws_and_comments(p);
                if (*p != ';') { free(path); snprintf(errbuf, errlen, "Expected ';' after readfile"); return NULL; }
                p++;
                *flags |= FLAG_FILE_IO;
                if (!buf_push(out, OPCODE_READFILE)) { free(path); snprintf(errbuf, errlen, "OOM"); return NULL; }
                if (!buf_push_bytes(out, path, plen)) { free(path); snprintf(errbuf, errlen, "OOM"); return NULL; }
                if (!buf_push(out, 0x00)) { free(path); snprintf(errbuf, errlen, "OOM"); return NULL; }
                free(path);
                continue;
            } else if (strcmp(ident, "writefile") == 0) {
                unsigned char *path=NULL; size_t plen=0; unsigned char *data=NULL; size_t dlen=0;
                p = parse_string_or_var(p, &path, &plen, vars, var_count, errbuf, errlen); if (!p) { snprintf(errbuf, errlen, "Invalid path in writefile"); return NULL; }
                p = skip_ws_and_comments(p); if (*p != ',') { free(path); snprintf(errbuf, errlen, "Expected ','"); return NULL; } p++;
                p = skip_ws_and_comments(p); p = parse_string_or_var(p, &data, &dlen, vars, var_count, errbuf, errlen); if (!p) { free(path); free(data); snprintf(errbuf, errlen, "Invalid data in writefile"); return NULL; }
                p = skip_ws_and_comments(p); if (*p != ';') { free(path); free(data); snprintf(errbuf, errlen, "Expected ';' after writefile"); return NULL; } p++;
                *flags |= FLAG_FILE_IO;
                if (!buf_push(out, OPCODE_WRITEFILE)) { free(path); free(data); snprintf(errbuf, errlen, "OOM"); return NULL; }
                if (!buf_push_bytes(out, path, plen)) { free(path); free(data); snprintf(errbuf, errlen, "OOM"); return NULL; }
                if (!buf_push(out, 0x00)) { free(path); free(data); snprintf(errbuf, errlen, "OOM"); return NULL; }
                if (!buf_push_bytes(out, data, dlen)) { free(path); free(data); snprintf(errbuf, errlen, "OOM"); return NULL; }
                if (!buf_push(out, 0x00)) { free(path); free(data); snprintf(errbuf, errlen, "OOM"); return NULL; }
                free(path); free(data);
                continue;
            } else if (strcmp(ident, "printbuf") == 0) {
                p = skip_ws_and_comments(p); if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after printbuf"); return NULL; } p++;
                *flags |= FLAG_CONSOLE;
                if (!buf_push(out, OPCODE_PRINTBUF)) { snprintf(errbuf, errlen, "OOM"); return NULL; }
                continue;
            } else if (strcmp(ident, "printlast") == 0) {
                p = skip_ws_and_comments(p); if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after printlast"); return NULL; } p++;
                *flags |= FLAG_CONSOLE;
                if (!buf_push(out, OPCODE_PRINTLAST)) { snprintf(errbuf, errlen, "OOM"); return NULL; }
                continue;
            } else if (strcmp(ident, "writebuf") == 0) {
                unsigned char *path = NULL; size_t plen = 0;
                p = parse_string_or_var(p, &path, &plen, vars, var_count, errbuf, errlen);
                if (!p) { snprintf(errbuf, errlen, "Invalid path for writebuf"); return NULL; }
                p = skip_ws_and_comments(p);
                if (*p != ';') { free(path); snprintf(errbuf, errlen, "Expected ';' after writebuf"); return NULL; }
                p++;
                *flags |= FLAG_FILE_IO;
                if (!buf_push(out, OPCODE_WRITEBUF)) { free(path); snprintf(errbuf, errlen, "OOM"); return NULL; }
                if (!buf_push_bytes(out, path, plen)) { free(path); snprintf(errbuf, errlen, "OOM"); return NULL; }
                if (!buf_push(out, 0x00)) { free(path); snprintf(errbuf, errlen, "OOM"); return NULL; }
                free(path);
                continue;
            } else if (strcmp(ident, "breakiflast") == 0) {
                /* Special for fragment: emit JEQ_LAST with placeholder rel (0x0000).
                 *               Caller must patch this placeholder to jump to end-of-loop.
                 *               Encoding: 0x51 <byte> <lo> <hi>
                 */
                int val = 0;
                p = parse_int_or_var(p, &val, vars, var_count, errbuf, errlen);
                if (!p) { snprintf(errbuf, errlen, "Invalid number for breakiflast"); return NULL; }
                p = skip_ws_and_comments(p); if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after breakiflast"); return NULL; } p++;
                if (!buf_push(out, OPCODE_JEQ_LAST)) { snprintf(errbuf, errlen, "OOM"); return NULL; }
                if (!buf_push(out, (unsigned char)val)) { snprintf(errbuf, errlen, "OOM"); return NULL; }
                /* placeholder rel bytes (we'll fill later) */
                int placeholder_offset = (int)out->len;
                if (!buf_push(out, 0x00) || !buf_push(out, 0x00)) { snprintf(errbuf, errlen, "OOM"); return NULL; }
                /* record placeholder offset so caller can patch it */
                if (!ilist_push(break_positions, placeholder_offset)) { snprintf(errbuf, errlen, "OOM"); return NULL; }
                continue;
            } else {
                snprintf(errbuf, errlen, "Unknown identifier in fragment: %s", ident);
                return NULL;
            }
    }

    return NULL; /* unreachable */
}

/* compile_source: top-level compiler that handles loop { ... } by using compile_fragment */
static int compile_source(const char *src, Buffer *out, char *errbuf, size_t errlen, unsigned char *out_flags) {
    char *p = (char*)src;
    buf_init(out);
    unsigned char flags = 0;
    /* variable table for this compilation unit */
    bx_var_t vars[MAX_VARS]; int var_count = 0; for (int _i=0; _i<MAX_VARS; ++_i) { vars[_i].name[0]=0; vars[_i].str_value=NULL; }

    while (1) {
        p = skip_ws_and_comments(p);
        if (!*p) break;

        char ident[64];
        p = parse_ident(p, ident, sizeof(ident));
        if (ident[0] == '\0') { snprintf(errbuf, errlen, "Expected identifier at: %.20s", p); buf_free(out); return 1; }

        if (strcmp(ident, "loop") == 0) {
            p = skip_ws_and_comments(p);
            if (*p != '{') { snprintf(errbuf, errlen, "Expected '{' after loop"); buf_free(out); return 2; }
            p++; /* consume '{' */

            /* compile body into temporary buffer */
            Buffer body;
            buf_init(&body);
            IntList break_positions;
            ilist_init(&break_positions);

            char *after = compile_fragment(p, &body, errbuf, errlen, &flags, &break_positions, vars, var_count);
            if (!after) { /* error already set */ buf_free(&body); ilist_free(&break_positions); buf_free(out); return 3; }

            /* Now we know body length. We will emit body into out, but we must:
             *               - For each recorded break placeholder offset in body, compute relative jump to end-of-loop
             *                 The JEQ we emitted is located at body.pos = offset-3 (op + val + rel), placeholder offset points to rel low byte inside body.
             *               - Layout in final out buffer: [body bytes] [JMP_REL16 back to start]
             *               We'll emit body, then compute positions and patch rels, then emit JMP back to start.
             */

            /* compute start position in out (IP where body begins) */
            int body_start = (int)out->len;

            /* append body bytes to out */
            if (!buf_push_bytes(out, body.data, body.len)) { snprintf(errbuf, errlen, "OOM"); buf_free(&body); ilist_free(&break_positions); buf_free(out); return 99; }

            /* compute end position (IP after body) */
            int end_of_body = (int)out->len;

            /* patch each break placeholder: placeholder is inside out at (body_start + placeholder_offset)
             *               We need to set rel = (end_of_body + size_of_jmp_padding_after_break) - (placeholder_ip_after_rel)
             *               Since JEQ consumes (1 opcode + 1 byte val + 2 bytes rel) => rel is relative to IP after those 2 bytes
             *               The IP after rel = (body_start + placeholder_offset + 2) (because placeholder_offset points to low byte of rel)
             *               So desired rel = (end_of_body + 0) - (body_start + placeholder_offset + 2)
             */
            for (int i = 0; i < break_positions.count; ++i) {
                int ph = break_positions.pos[i]; /* offset within body where low-byte of rel sits */
                int rel = (end_of_body) - (body_start + ph + 2);
                short srel = (short)rel;
                unsigned char lo = (unsigned char)(srel & 0xFF);
                unsigned char hi = (unsigned char)((srel >> 8) & 0xFF);
                /* write into out at body_start + ph */
                out->data[body_start + ph] = lo;
                out->data[body_start + ph + 1] = hi;
            }

            /* Now emit a backward JMP to loop start:
             *               JMP_REL16 rel_back where rel_back = body_start - (out_ip_after_jmp)
             *               out_ip_after_jmp = end_of_body + 3 (opcode + 2 bytes) BUT we'll append jmp now, so compute rel relative to IP after the 2 bytes we write
             *               After we push opcode and two bytes, ip after rel would be (end_of_body + 3)
             *               So to jump back to body_start: rel = body_start - (end_of_body + 3)
             */
            if (!buf_push(out, OPCODE_JMP_REL16)) { snprintf(errbuf, errlen, "OOM"); buf_free(&body); ilist_free(&break_positions); buf_free(out); return 99; }
            int rel_back = body_start - ((int)out->len + 2); /* because we will write 2 bytes now, ip after rel will be out->len + 2 */
            short srel_back = (short)rel_back;
            unsigned char lo = (unsigned char)(srel_back & 0xFF);
            unsigned char hi = (unsigned char)((srel_back >> 8) & 0xFF);
            if (!buf_push(out, lo) || !buf_push(out, hi)) { snprintf(errbuf, errlen, "OOM"); buf_free(&body); ilist_free(&break_positions); buf_free(out); return 99; }

            /* free temp */
            buf_free(&body);
            ilist_free(&break_positions);

            /* advance p to after the '}' (compile_fragment returns pointer to after '}') */
            p = after;
            continue;
        } else if (strcmp(ident, "var") == 0) {
            /* parse: var NAME = <literal>; */
            char vname[64];
            p = parse_ident(p, vname, sizeof(vname));
            if (vname[0] == '\0') { snprintf(errbuf, errlen, "Expected variable name after 'var'"); buf_free(out); return 25; }
            p = skip_ws_and_comments(p);
            if (*p != '=') { snprintf(errbuf, errlen, "Expected '=' after variable name"); buf_free(out); return 26; }
            p++;
            p = skip_ws_and_comments(p);
            if (var_count + 1 > MAX_VARS) { snprintf(errbuf, errlen, "Too many variables"); buf_free(out); return 27; }
            bx_var_t *vv = &vars[var_count];
            vv->name[0] = '\0'; vv->type = VAR_INT; vv->int_value = 0; vv->str_value = NULL;
            if (*p == '\"') {
                unsigned char *s = NULL; size_t sl = 0;
                p = parse_string_literal(p, &s, &sl);
                if (!p) { snprintf(errbuf, errlen, "Invalid string literal in var"); buf_free(out); return 28; }
                /* store null-terminated copy */
                vv->type = VAR_STR;
                vv->str_value = malloc(sl + 1);
                if (!vv->str_value) { free(s); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 29; }
                memcpy(vv->str_value, s, sl); vv->str_value[sl] = '\0';
                free(s);
            } else if (*p == '\'') {
                unsigned char ch = 0;
                char *np = parse_char_literal(p, &ch);
                if (!np) { snprintf(errbuf, errlen, "Invalid char literal in var"); buf_free(out); return 30; }
                p = np;
                vv->type = VAR_INT;
                vv->int_value = (int)ch;
            } else {
                int ival = 0;
                p = parse_int_literal(p, &ival);
                if (!p) { snprintf(errbuf, errlen, "Invalid int literal in var"); buf_free(out); return 31; }
                vv->type = VAR_INT;
                vv->int_value = ival;
            }
            p = skip_ws_and_comments(p);
            if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after var declaration"); buf_free(out); return 32; }
            p++;
            /* copy name */
            strncpy(vv->name, vname, sizeof(vv->name)-1); vv->name[sizeof(vv->name)-1]=0;
            var_count++;
            continue;
        }

        /* top-level statements (same as in fragment but without breakiflast) */
        if (strcmp(ident, "print") == 0) {
            unsigned char *str = NULL; size_t slen = 0;
            p = parse_string_or_var(p, &str, &slen, vars, var_count, errbuf, errlen);
            if (!p) { snprintf(errbuf, errlen, "Invalid string literal"); buf_free(out); return 2; }
            p = skip_ws_and_comments(p);
            if (*p != ';') { free(str); snprintf(errbuf, errlen, "Expected ';' after print"); buf_free(out); return 3; }
            p++;
            if (!buf_push(out, OPCODE_PRINT)) { free(str); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            if (!buf_push_bytes(out, str, slen)) { free(str); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            if (!buf_push(out, 0x00)) { free(str); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            free(str);
            continue;
        } else if (startsWith(ident, "#")) {
            continue;
        } else if (strcmp(ident, "putchar") == 0) {
            unsigned char ch;
            p = parse_char_literal(p, &ch);
            if (!p) { snprintf(errbuf, errlen, "Invalid char literal"); buf_free(out); return 4; }
            p = skip_ws_and_comments(p);
            if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after putchar"); buf_free(out); return 5; }
            p++;
            if (!buf_push(out, OPCODE_PUTCHAR)) { snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            if (!buf_push(out, ch)) { snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            continue;
        } else if (strcmp(ident, "exit") == 0) {
            p = skip_ws_and_comments(p);
            if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after exit"); buf_free(out); return 6; }
            p++;
            if (!buf_push(out, OPCODE_EXIT)) { snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            continue;
        } else if (strcmp(ident, "clear") == 0) {
            p = skip_ws_and_comments(p);
            if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after clear"); buf_free(out); return 10; }
            p++;
            flags |= FLAG_CONSOLE;
            if (!buf_push(out, OPCODE_CLEAR)) { snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            continue;
        } else if (strcmp(ident, "setcursor") == 0) {
            int x = 0, y = 0;
            p = parse_int_or_var(p, &x, vars, var_count, errbuf, errlen);
            if (!p) { snprintf(errbuf, errlen, "Invalid X in setcursor"); buf_free(out); return 11; }
            p = skip_ws_and_comments(p);
            if (*p == ',') p++;
            p = parse_int_or_var(p, &y, vars, var_count, errbuf, errlen);
            if (!p) { snprintf(errbuf, errlen, "Invalid Y in setcursor"); buf_free(out); return 12; }
            p = skip_ws_and_comments(p);
            if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after setcursor"); buf_free(out); return 13; }
            p++;
            flags |= FLAG_CONSOLE;
            if (!buf_push(out, OPCODE_SETCURSOR)) { snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            if (!buf_push(out, (unsigned char)(x<0?0:(x>255?255:x)))) { snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            if (!buf_push(out, (unsigned char)(y<0?0:(y>255?255:y)))) { snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            continue;
        } else if (strcmp(ident, "readkey") == 0) {
            p = skip_ws_and_comments(p);
            if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after readkey"); buf_free(out); return 14; }
            p++;
            flags |= FLAG_CONSOLE;
            if (!buf_push(out, OPCODE_READKEY)) { snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            continue;
        } else if (strcmp(ident, "readfile") == 0) {
            unsigned char *path = NULL; size_t plen = 0;
            p = parse_string_or_var(p, &path, &plen, vars, var_count, errbuf, errlen);
            if (!p) { snprintf(errbuf, errlen, "Invalid string for readfile"); buf_free(out); return 15; }
            p = skip_ws_and_comments(p);
            if (*p != ';') { free(path); snprintf(errbuf, errlen, "Expected ';' after readfile"); buf_free(out); return 16; }
            p++;
            flags |= FLAG_FILE_IO;
            if (!buf_push(out, OPCODE_READFILE)) { free(path); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            if (!buf_push_bytes(out, path, plen)) { free(path); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            if (!buf_push(out, 0x00)) { free(path); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            free(path);
            continue;
        } else if (strcmp(ident, "writefile") == 0) {
            unsigned char *path = NULL; size_t plen = 0;
            unsigned char *data = NULL; size_t dlen = 0;
            p = parse_string_or_var(p, &path, &plen, vars, var_count, errbuf, errlen);
            if (!p) { snprintf(errbuf, errlen, "Invalid path string for writefile"); buf_free(out); return 17; }
            p = skip_ws_and_comments(p);
            if (*p != ',') { free(path); snprintf(errbuf, errlen, "Expected ',' after path in writefile"); buf_free(out); return 18; }
            p++;
            p = skip_ws_and_comments(p);
            p = parse_string_or_var(p, &data, &dlen, vars, var_count, errbuf, errlen);
            if (!p) { free(path); free(data); snprintf(errbuf, errlen, "Invalid data string for writefile"); buf_free(out); return 19; }
            p = skip_ws_and_comments(p);
            if (*p != ';') { free(path); free(data); snprintf(errbuf, errlen, "Expected ';' after writefile"); buf_free(out); return 20; }
            p++;
            flags |= FLAG_FILE_IO;
            if (!buf_push(out, OPCODE_WRITEFILE)) { free(path); free(data); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            if (!buf_push_bytes(out, path, plen)) { free(path); free(data); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            if (!buf_push(out, 0x00)) { free(path); free(data); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            if (!buf_push_bytes(out, data, dlen)) { free(path); free(data); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            if (!buf_push(out, 0x00)) { free(path); free(data); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            free(path); free(data);
            continue;
        } else if (strcmp(ident, "printbuf") == 0) {
            p = skip_ws_and_comments(p); if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after printbuf"); buf_free(out); return 21; } p++;
            flags |= FLAG_CONSOLE;
            if (!buf_push(out, OPCODE_PRINTBUF)) { snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            continue;
        } else if (strcmp(ident, "printlast") == 0) {
            p = skip_ws_and_comments(p); if (*p != ';') { snprintf(errbuf, errlen, "Expected ';' after printlast"); buf_free(out); return 22; } p++;
            flags |= FLAG_CONSOLE;
            if (!buf_push(out, OPCODE_PRINTLAST)) { snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            continue;
        } else if (strcmp(ident, "writebuf") == 0) {
            unsigned char *path = NULL; size_t plen = 0;
            p = parse_string_or_var(p, &path, &plen, vars, var_count, errbuf, errlen);
            if (!p) { snprintf(errbuf, errlen, "Invalid path string for writebuf"); buf_free(out); return 23; }
            p = skip_ws_and_comments(p);
            if (*p != ';') { free(path); snprintf(errbuf, errlen, "Expected ';' after writebuf"); buf_free(out); return 24; }
            p++;
            flags |= FLAG_FILE_IO;
            if (!buf_push(out, OPCODE_WRITEBUF)) { free(path); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            if (!buf_push_bytes(out, path, plen)) { free(path); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            if (!buf_push(out, 0x00)) { free(path); snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
            free(path);
            continue;
        } else {
            snprintf(errbuf, errlen, "Unknown identifier: %s", ident);
            buf_free(out);
            return 7;
        }
    }

    /* ensure final instruction exit */
    if (out->len == 0 || out->data[out->len - 1] != OPCODE_EXIT) {
        if (!buf_push(out, OPCODE_EXIT)) { snprintf(errbuf, errlen, "OOM"); buf_free(out); return 99; }
    }

    /* free variable string storage */
    for (int _vi=0; _vi<var_count; ++_vi) if (vars[_vi].str_value) { free(vars[_vi].str_value); vars[_vi].str_value = NULL; }

    *out_flags = flags;
    return 0;
}

int main(int argc, char **argv) {
    if (argc < 3) {
        fprintf(stderr, "Usage: %s <input.bxlang> <output.bx>\n", argv[0]);
        return 1;
    }
    const char *inpath = argv[1];
    const char *outpath = argv[2];

    size_t src_len;
    char *src = read_file(inpath, &src_len);
    if (!src) {
        fprintf(stderr, "Failed to read input file: %s\n", inpath);
        return 2;
    }

    Buffer code;
    char err[256];
    unsigned char flags = 0;
    int rc = compile_source(src, &code, err, sizeof(err), &flags);
    if (rc != 0) {
        fprintf(stderr, "Compile error: %s\n", err);
        free(src);
        return 3;
    }

    FILE *fo = fopen(outpath, "wb");
    if (!fo) { fprintf(stderr, "Failed to open output: %s\n", outpath); free(src); buf_free(&code); return 4; }

    /* header: magic 'BEXE', version=1, flags byte, codeLen (4 bytes LE), 6 bytes pad to 16 */
    unsigned char magic[4] = { 'B','E','X','E' };
    fwrite(magic,1,4,fo);
    fputc(1, fo);           /* version */
    fputc((int)flags, fo);  /* flags */
    write_u32_le(fo, (uint32_t)code.len);
    unsigned char pad[6] = {0,0,0,0,0,0};
    fwrite(pad,1,6,fo);

    if (fwrite(code.data,1,code.len,fo) != code.len) { fprintf(stderr, "Write error\n"); fclose(fo); free(src); buf_free(&code); return 5; }
    fclose(fo);

    printf("Wrote %s (code bytes: %zu) flags=0x%02X\n", outpath, code.len, flags);

    free(src);
    buf_free(&code);
    return 0;
}

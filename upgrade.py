import os

def replace_in_cs_files(root_dir):
    for folder, _, files in os.walk(root_dir):
        for file in files:
            if file.endswith(".cs"):
                path = os.path.join(folder, file)
                with open(path, "r", encoding="utf-8") as f:
                    content = f.read()

                new_content = content.replace("BadFS4", "TailsFS")

                if new_content != content:
                    with open(path, "w", encoding="utf-8") as f:
                        f.write(new_content)
                    print(f"Updated: {path}")

if __name__ == "__main__":
    replace_in_cs_files(".")  # Replace "." with any starting directory you want

import os

# Initialize an empty list to store the paths of all objects
objects_list = []

# Define folders to exclude
exclude_folders = [
    "node_modules",
    "venv",
    ".venv",
    "__pycache__",
    "dist",
    "build",
    ".gpt",
    "obj",
    "bin",
    "obj",
    "dist",
    ".git",
]

# Allowed file extensions
allowed_extensions = (
    ".cjs",
    ".class",
    ".conf",
    ".cfg",
    ".css",
    ".cs",
    ".csproj",
    ".dockerfile",
    ".env",
    ".gitignore",
    ".htm",
    ".html",
    ".ini",
    ".java",
    ".js",
    ".json",
    ".jsonl",
    ".jsp",
    ".jspx",
    ".jsx",
    ".less",
    ".log",
    ".md",
    ".mjs",
    ".pyd",
    ".py",
    ".pyc",
    ".pxd",
    ".pxi",
    ".pyw",
    ".pyx",
    ".pyo",
    ".sass",
    ".scss",
    ".sh",
    ".sln",
    ".srt",
    ".svelte",
    ".ts",
    ".tsx",
    ".txt",
    ".vue",
    ".xhtml",
    ".yaml",
    ".yml",
)


def generate_tree(directory, prefix=""):
    """
    Recursively generate a tree structure of all files and directories.
    """
    tree = []
    try:
        entries = os.listdir(directory)
    except PermissionError:
        return ["[ACCESS DENIED]"]

    for idx, entry in enumerate(entries):
        full_path = os.path.join(directory, entry)
        connector = "├── " if idx < len(entries) - 1 else "└── "
        tree.append(f"{prefix}{connector}{entry}")
        if (
            os.path.isdir(full_path)
            and not entry.startswith(".")
            and entry not in exclude_folders
        ):
            subtree = generate_tree(
                full_path, prefix + ("│   " if idx < len(entries) - 1 else "    ")
            )
            tree.extend(subtree)
    return tree


# Generate the tree structure for the current directory
# directory_tree = generate_tree("./compute.rhino3d/src")

# Write the tree structure and filtered list of objects to 'dump.txt'
with open("./.gpt/dump.txt", "w", encoding="utf-8") as f:
    # Write the directory tree at the beginning
    # f.write("&&& DIRECTORY TREE &&&\n\n")
    # f.write("\n".join(directory_tree) + "\n\n")

    # Walk through the current directory and its subdirectories
    for dirpath, dirnames, filenames in os.walk(r"./"):
        # Skip directories starting with "." or in the exclude list
        dirnames[:] = [
            d for d in dirnames if not d.startswith(".") and d not in exclude_folders
        ]

        # Add files with allowed extensions to the list
        for name in filenames:
            objects_list.append(os.path.join(dirpath, name))

    # Write filtered objects and their content to the dump file
    for obj in objects_list:
        if os.path.isfile(obj):
            try:
                with open(obj, "r", encoding="utf-8") as o:
                    if obj.endswith(allowed_extensions):
                        content = o.read()
                        if len(content) > 50000:
                            content = content[:50000] + "\n................................"
                    else:
                        content = o.read()[:20] + "\n................................"
                    f.write("<" + obj + ">\n" + content + "\n\n")
            except (IOError, UnicodeDecodeError) as e:
                f.write(f"&&& FILE: {obj}\n&&& ERROR: Could not read file: {e}\n\n")

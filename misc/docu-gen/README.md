# Grasshopper Python SDK Doc Generator

Generate a **single Markdown reference** of everything callable from a **GhPython** node—accurate to the Rhino/Grasshopper version and plug-ins you currently have loaded.

* RhinoCommon & Grasshopper public APIs (types, fields, ctors, properties, methods, events)
* XML doc summaries/remarks + parameter/returns docs (when available)
* `rhinoscriptsyntax` + `ghpythonlib` (`components`, `parallel`, `trees`) signatures & docstrings
* *(Optional)* catalog of loaded Grasshopper components (categories, IO, GUIDs)
* Works inside **Rhino 7 (IronPython)** and **Rhino 8 (CPython)** on macOS & Windows

---

## Why?

GhPython’s usable API depends on your installed Rhino/GH version and loaded plug-ins. This tool runs **inside your Grasshopper session**, reflects the real assemblies, parses the shipped XML docs, and emits a rich Markdown file you can feed to LLM agents or use as local documentation.

---

## Repository layout
```text
gh-docu-gen/
├─ gh_docu_gen.gh # Grasshopper file (wires inputs to the Python script)
├─ gh_docu_gen.py # Extractor script (runs inside GhPython)
└─ README.md
```


---

## Quick Start

1. Open **Grasshopper** and load `gh_docu_gen/gh_docu_gen.gh`.
2. Ensure the GhPython component points to the external script:
   - `gh_docu_gen.py` (absolute path).  
     If you moved the repo, update the path in the Text Panel (or the file path parameter) inside the GH file.
3. Recompute. The doc is generated using the settings defined **inside** `gh_docu_gen.py`.

> No Grasshopper inputs are required beyond the path to the Python file.

---

## Configure output & options (in code)

Edit the top of `gh_docu_gen.py` to set your preferences:

```python
# --- User settings (edit here) ---

OUT_PATH = r"/Users/<you>/Desktop/gh_python_sdk.md"  # macOS example
# OUT_PATH = r"C:\Users\<you>\Desktop\gh_python_sdk.md"  # Windows example

INCLUDE_PLUGINS     = False   # scan loaded non-System plugin assemblies
DECLARED_ONLY       = True    # only members declared on the type (smaller)
INCLUDE_COMPONENTS  = False   # append GH components catalog (heavier)
INCLUDE_MEMBER_XML  = True    # parse DLL XML docs for member summaries/params/returns


> **Recommended first run:** `declared_only=True`, `include_plugins=False`, `include_components=False`, `include_member_xml=True`.

---

## Inputs (reference)

| Input                 | Type    | Default | Description |
|----------------------|---------|---------|-------------|
| `out_path`           | Text    | —       | Absolute path for the Markdown output. The folder will be created if needed. |
| `include_plugins`    | Boolean | `False` | Also scan **all loaded non-System assemblies** (plug-ins). Increases size/time. |
| `declared_only`      | Boolean | `True`  | Only members **declared** on each type. Set `False` to include **inherited** members (much larger). |
| `include_components` | Boolean | `False` | Append a **Grasshopper components catalog** (category → subcategory → component) with IO & GUIDs. Heavier. |
| `include_member_xml` | Boolean | `True`  | Parse XML docs for **member** summaries/params/returns (keep on for best docs). |

---

## What gets documented

### .NET API (RhinoCommon / Grasshopper / plug-ins)

* **Types:** class/struct/enum/interface/delegate, with flags (*abstract*, *sealed*, *static*), base type, implemented interfaces, nested types
* **Members:** fields (incl. enum values), constructors, properties (accessors & indexers), methods (with `ref/out/optional` & defaults), events
* **Attributes:** `[Obsolete]` messages are surfaced inline
* **Docs:** XML summaries + remarks; per-parameter docs; returns text (when XML sidecars are installed)

### Python helpers

* `rhinoscriptsyntax` – function names, signatures, docstrings
* `ghpythonlib.components / parallel / trees` – proxies/signatures/docstrings

### *(Optional)* Grasshopper component catalog

* Category / Subcategory / Name / Nick / **GUID**
* Inputs/Outputs: name, nickname, type name, optional flag, access mode

---

## Output structure (Markdown)

* Environment banner (Python/Rhino/Grasshopper versions; flags used)
* Assemblies → Namespaces → Types → Members  
  * Inline summaries, obsolete notes, parameter/returns docs
* Python modules (`rhinoscriptsyntax`, `ghpythonlib.*`)
* *(Optional)* Grasshopper components catalog

> The file can be large (100k+ lines) if you include plug-ins and inherited members.

---

## Performance tips

* Keep `declared_only=True` for compact docs.
* Disable `include_plugins` for a core Rhino/GH snapshot.
* Enable `include_components` only when you need the GH components list (it instantiates components to read IO).
* With huge plug-in stacks, generate separate docs per configuration.

---

## Troubleshooting

* **“Please provide a valid 'out_path' string.”** → Use an **absolute** path (e.g., `/Users/me/Desktop/file.md`).
* **Permission errors** → Write to Desktop/Documents or a folder you own.
* **Summaries missing** → Your install may not include `RhinoCommon.xml` / `Grasshopper.xml`. Signatures still render; installing full Rhino 8 or copying the XMLs next to the DLLs improves docs.
* **Very slow / huge output** → Start with `include_plugins=False`, `declared_only=True`, `include_components=False`.

---

## How it works (internals)

* Runs **inside Grasshopper** (GhPython), reflecting the assemblies loaded in your session.
* Parses DLL **XML sidecars** to attach summaries/remarks/param/returns.
* Introspects Python modules (`rhinoscriptsyntax`, `ghpythonlib`).
* *(Optional)* Builds a GH components catalog by instantiating components and reading their IO metadata.

---

## Using the docs with LLM agents

* Commit the generated `gh_python_sdk.md` to your repo (or bundle with your MCP).
* Chunk by **namespace** or **type** for retrieval.
* Add a short **GhPython usage primer** (e.g., `ghenv.Component`, `scriptcontext.doc`, DataTree basics) to your context.

# GhPython SDK Doc Extractor (fixed assembly detection)
# Works in Rhino 7 (IronPython) and Rhino 8 (CPython)
# Inputs:
#   out_path: str (required)
#   include_plugins: bool (optional, default False)
#   declared_only: bool (optional, default True)

#! python3
# --*-- coding: utf-8 --*--

import sys, os, datetime

# --- Environment guards ---
import Rhino, Grasshopper
is_cpython = sys.version_info >= (3,0)
py_version = "{}.{}.{}".format(sys.version_info.major, sys.version_info.minor, sys.version_info.micro)

try:
    import inspect
    HAVE_SIGNATURE = hasattr(inspect, "signature")
except Exception:
    HAVE_SIGNATURE = False

import System
from System.Reflection import BindingFlags
from System import AppDomain

try:
    import xml.etree.ElementTree as ET
except Exception:
    ET = None

# --- Defaults for GH inputs (if unwired) ---
try: out_path
except NameError: out_path = os.path.expanduser("~/Desktop/gh_python_sdk.md")

try: include_plugins
except NameError: include_plugins = False

try: declared_only
except NameError: declared_only = True

if not out_path or not isinstance(out_path, basestring if not is_cpython else str):
    raise Exception("Please provide a valid 'out_path' string.")

out_dir = os.path.dirname(out_path)
if out_dir and not os.path.isdir(out_dir):
    os.makedirs(out_dir)

# --- Helpers ---
def kind_of_type(t):
    try:
        if t.IsEnum: return "enum"
        if t.IsInterface: return "interface"
        if t.IsValueType and not t.IsEnum: return "struct"
        if t.IsClass: return "class"
        try:
            if t.IsSubclassOf(System.Delegate): return "delegate"
        except: pass
    except: pass
    return "type"

def short_type_name(t):
    try:
        n = t.FullName or t.Name
    except:
        n = str(t)
    if not n: return "object"
    return (n.replace("`1","").replace("`2","").replace("`3","")
             .replace("[[","<").replace("]]",">").replace("[","<").replace("]",">"))

def method_signature(m):
    try: ret = short_type_name(m.ReturnType) if hasattr(m, "ReturnType") else "void"
    except: ret = "void"
    name = getattr(m, "Name", "<?>")
    try:
        pars = [ "{} {}".format(short_type_name(p.ParameterType), p.Name) for p in m.GetParameters() ]
        params_str = ", ".join(pars)
    except:
        params_str = ""
    return "{} {}({})".format(ret, name, params_str)

def ctor_signature(ctor):
    try:
        pars = [ "{} {}".format(short_type_name(p.ParameterType), p.Name) for p in ctor.GetParameters() ]
        params_str = ", ".join(pars)
    except:
        params_str = ""
    return "{}({})".format(ctor.DeclaringType.Name, params_str)

def property_signature(p):
    try: tname = short_type_name(p.PropertyType)
    except: tname = "object"
    name = getattr(p, "Name", "<?>")
    acc = []
    try:
        if p.CanRead: acc.append("get")
        if p.CanWrite: acc.append("set")
    except: pass
    return "{} {} {{ {} }}".format(tname, name, "; ".join(acc) if acc else "")

def event_signature(ev):
    try: handlertype = short_type_name(ev.EventHandlerType)
    except: handlertype = "EventHandler"
    return "{} {}".format(handlertype, getattr(ev, "Name", "<?>"))

def get_flags():
    flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance
    if declared_only:
        flags |= BindingFlags.DeclaredOnly
    return flags

def load_xml_lookup_for_assembly(asm):
    if ET is None: return {}
    try: loc = asm.Location
    except: return {}
    if not loc: return {}
    xml_path = os.path.splitext(loc)[0] + ".xml"
    if not os.path.isfile(xml_path): return {}
    try:
        root = ET.parse(xml_path).getroot()
        members = root.find("members")
        lookup = {}
        if members is None: return lookup
        for m in members.findall("member"):
            name = m.get("name") or ""
            if name.startswith("T:"):
                summ = m.findtext("summary") or ""
                summ = " ".join(summ.split())
                if summ:
                    lookup[name[2:]] = summ
        return lookup
    except Exception:
        return {}

def find_assembly(name):
    """Find a loaded assembly by simple name (e.g. 'RhinoCommon', 'Grasshopper')."""
    for a in AppDomain.CurrentDomain.GetAssemblies():
        try:
            if (a.GetName().Name or "") == name:
                return a
        except:
            pass
    return None

# --- Robust assembly selection (FIX) ---
rhino_asm = find_assembly("RhinoCommon")
gh_asm    = find_assembly("Grasshopper")

assemblies = [a for a in (rhino_asm, gh_asm) if a is not None]

def asm_version_str(a):
    try:
        v = a.GetName().Version
        return str(v) if v else "unknown"
    except:
        return "unknown"

rhino_version = str(Rhino.RhinoApp.Version)
gh_version    = asm_version_str(gh_asm)

if include_plugins:
    for a in AppDomain.CurrentDomain.GetAssemblies():
        try:
            n = a.GetName().Name or ""
        except:
            n = ""
        if not n or a in assemblies: continue
        if n.startswith("System") or n.startswith("Microsoft"): continue
        assemblies.append(a)

# --- Reflect & collect ---
flags = get_flags()
data = {
    "meta": {
        "generatedAt": datetime.datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S UTC"),
        "python": "CPython {}".format(py_version) if is_cpython else "IronPython {}".format(py_version),
        "rhino": rhino_version,
        "grasshopper": gh_version,
        "declared_only": bool(declared_only),
        "include_plugins": bool(include_plugins),
    },
    "assemblies": []
}


asm_xml_maps = {}
for asm in assemblies:
    try: asm_xml_maps[asm] = load_xml_lookup_for_assembly(asm)
    except Exception: asm_xml_maps[asm] = {}

for asm in assemblies:
    try:
        asm_name = asm.GetName().Name or "<unknown>"
        asm_ver  = str(asm.GetName().Version) if asm.GetName().Version else ""
    except:
        asm_name, asm_ver = "<unknown>", ""
    asm_entry = {"name": asm_name, "version": asm_ver, "namespaces": {}}

    try:
        types = asm.GetExportedTypes()
    except Exception:
        types = []

    for t in types:
        try:
            if not t.IsPublic: continue
        except: pass

        ns = getattr(t, "Namespace", None) or "<global>"
        ns_bucket = asm_entry["namespaces"].setdefault(ns, [])

        t_entry = {
            "name": t.FullName or t.Name,
            "kind": kind_of_type(t),
            "summary": asm_xml_maps.get(asm, {}).get(t.FullName or "", ""),
            "constructors": [],
            "properties": [],
            "methods": [],
            "events": []
        }

        # constructors
        try:
            for ctor in t.GetConstructors(flags):
                t_entry["constructors"].append(ctor_signature(ctor))
        except Exception:
            pass

        # properties
        try:
            for p in t.GetProperties(flags):
                try:
                    if hasattr(p, "GetIndexParameters") and p.GetIndexParameters() and len(p.GetIndexParameters())>0:
                        continue  # skip indexers to reduce noise
                except: pass
                t_entry["properties"].append(property_signature(p))
        except Exception:
            pass

        # methods
        try:
            for m in t.GetMethods(flags):
                if getattr(m, "IsSpecialName", False):  # skip get_/set_/add_/remove_
                    continue
                t_entry["methods"].append(method_signature(m))
        except Exception:
            pass

        # events
        try:
            for ev in t.GetEvents(flags):
                t_entry["events"].append(event_signature(ev))
        except Exception:
            pass

        ns_bucket.append(t_entry)

    data["assemblies"].append(asm_entry)

# --- Python modules: rs + ghpythonlib ---
def dump_py_module(mod, name):
    out = {"name": name, "functions": []}
    try:
        import inspect
    except Exception:
        inspect = None

    try:
        members = inspect.getmembers(mod) if inspect else [(n, getattr(mod,n)) for n in dir(mod)]
    except Exception:
        return out

    for n, obj in members:
        if n.startswith("_"): continue
        try:
            is_func = (inspect.isfunction(obj) or inspect.ismethod(obj) or callable(obj)) if inspect else callable(obj)
        except:
            is_func = False
        if not is_func: continue

        sig = None
        if inspect and HAVE_SIGNATURE:
            try: sig = str(inspect.signature(obj))
            except Exception: pass
        if sig is None and inspect and hasattr(inspect, "getargspec"):
            try:
                argspec = inspect.getargspec(obj)  # IronPython fallback
                sig = "(" + ", ".join(argspec.args or []) + ")"
            except Exception:
                sig = None

        try: doc = inspect.getdoc(obj) or "" if inspect else ""
        except Exception: doc = ""

        out["functions"].append({"name": n, "signature": sig, "doc": doc})
    return out

py_modules = []
try:
    import rhinoscriptsyntax as rs
    py_modules.append(dump_py_module(rs, "rhinoscriptsyntax"))
except Exception:
    pass
try:
    import ghpythonlib.components as comp
    py_modules.append(dump_py_module(comp, "ghpythonlib.components"))
except Exception:
    pass
try:
    import ghpythonlib.parallel as par
    py_modules.append(dump_py_module(par, "ghpythonlib.parallel"))
except Exception:
    pass
try:
    import ghpythonlib.trees as trees
    py_modules.append(dump_py_module(trees, "ghpythonlib.trees"))
except Exception:
    pass

# --- Render Markdown (simple, inline) ---
def md_escape(s):
    try: return s.replace("<","&lt;").replace(">","&gt;")
    except: return s

lines = []
meta = data["meta"]
lines += [
    "# Grasshopper Python SDK Documentation",
    "",
    "Generated: {}".format(meta["generatedAt"]),
    "",
    "## Environment",
    "- Python: {}".format(meta["python"]),
    "- Rhino: {}".format(meta["rhino"]),
    "- Grasshopper: {}".format(meta["grasshopper"]),
    "- Declared-only: {}".format(meta["declared_only"]),
    "- Include plugins: {}".format(meta["include_plugins"]),
    "",
    "---",
    ""
]

for asm in data["assemblies"]:
    lines.append("## Assembly `{}` ({})".format(asm["name"], asm["version"]))
    for ns in sorted(asm["namespaces"].keys()):
        lines.append("")
        lines.append("### Namespace `{}`".format(ns))
        types = sorted(asm["namespaces"][ns], key=lambda x: x["name"])
        for t in types:
            lines.append("")
            lines.append("#### {} `{}`".format(t["kind"], t["name"]))
            if t.get("summary"):
                lines.append("> " + md_escape(t["summary"]))
                lines.append("")
            if t["constructors"]:
                lines.append("**Constructors**")
                for s in t["constructors"]:
                    lines.append("- `{}`".format(md_escape(s)))
                lines.append("")
            if t["properties"]:
                lines.append("**Properties**")
                for s in t["properties"]:
                    lines.append("- `{}`".format(md_escape(s)))
                lines.append("")
            if t["methods"]:
                lines.append("**Methods**")
                for s in t["methods"]:
                    lines.append("- `{}`".format(md_escape(s)))
                lines.append("")
            if t["events"]:
                lines.append("**Events**")
                for s in t["events"]:
                    lines.append("- `{}`".format(md_escape(s)))
                lines.append("")
    lines.append("")
    lines.append("---")
    lines.append("")

lines.append("## Python Modules")
for mod in py_modules:
    lines.append("")
    lines.append("### {}".format(mod["name"]))
    for f in sorted(mod["functions"], key=lambda it: it["name"].lower()):
        sig = f["signature"] or ""
        lines.append("- **{}** `{}`".format(f["name"], md_escape(sig)))
    lines.append("")

# --- Write file ---
try:
    with open(out_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    msg = "OK: wrote markdown to '{}' ({} lines)".format(out_path, len(lines))
except Exception as e:
    msg = "ERROR writing '{}': {}".format(out_path, e)

print(msg)
try: a = msg
except: pass

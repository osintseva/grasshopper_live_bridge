# GhPython SDK Doc Extractor — detailed version
# Works in Rhino 7 (IronPython) and Rhino 8 (CPython)
# Inputs:
#   out_path: str (required)
#   include_plugins: bool (optional, default False)
#   declared_only: bool (optional, default True)
#   include_components: bool (optional, default False)  # NEW: GH components catalog
#   include_member_xml: bool (optional, default True)   # NEW: parse XML for members too

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
from System.Reflection import BindingFlags, MemberTypes
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

try: include_components
except NameError: include_components = False  # heavy; turn on if you want GH components catalog

try: include_member_xml
except NameError: include_member_xml = True

if not out_path or not isinstance(out_path, basestring if not is_cpython else str):
    raise Exception("Please provide a valid 'out_path' string.")

out_dir = os.path.dirname(out_path)
if out_dir and not os.path.isdir(out_dir):
    os.makedirs(out_dir)

# --- Helpers (rendering & reflection) ---
def kind_of_type(t):
    try:
        if t.IsEnum: return "enum"
        if t.IsInterface: return "interface"
        if t.IsValueType and not t.IsEnum: return "struct"
        # delegate?
        try:
            if t.IsSubclassOf(System.Delegate): return "delegate"
        except: pass
        if t.IsClass: return "class"
    except: pass
    return "type"

def flag_str(t):
    bits = []
    try:
        if getattr(t, "IsAbstract", False) and getattr(t, "IsSealed", False):
            bits.append("static")
        else:
            if getattr(t, "IsAbstract", False): bits.append("abstract")
            if getattr(t, "IsSealed",   False): bits.append("sealed")
    except: pass
    return (" [" + ", ".join(bits) + "]") if bits else ""

def short_type_name(t):
    try:
        n = t.FullName or t.Name
    except:
        n = str(t)
    if not n: return "object"
    # readable generics
    n = (n.replace("`1","").replace("`2","").replace("`3","")
           .replace("[[","<").replace("]]",">").replace("[","<").replace("]",">"))
    return n

def param_label(p):
    mods = []
    try:
        if p.IsOut: mods.append("out")
        elif p.ParameterType.IsByRef: mods.append("ref")
    except: pass
    try:
        if p.IsOptional: mods.append("optional")
    except: pass
    base = "{} {}".format(short_type_name(p.ParameterType.GetElementType() if p.ParameterType.IsByRef else p.ParameterType), p.Name)
    if mods: base = "{} {}".format(" ".join(mods), base)
    try:
        if p.IsOptional and p.DefaultValue is not System.DBNull.Value:
            base += " = {}".format(repr(p.DefaultValue))
    except: pass
    return base

def method_signature(m):
    try: ret = short_type_name(m.ReturnType) if hasattr(m, "ReturnType") else "void"
    except: ret = "void"
    name = getattr(m, "Name", "<?>")
    try:
        pars = [ param_label(p) for p in m.GetParameters() ]
        params_str = ", ".join(pars)
    except:
        params_str = ""
    # method qualifiers
    q = []
    try:
        if m.IsStatic: q.append("static")
        if m.IsAbstract: q.append("abstract")
        elif m.IsVirtual:
            # detect override
            try:
                base_def = m.GetBaseDefinition()
                if base_def is not None and base_def.DeclaringType != m.DeclaringType:
                    q.append("override")
                else:
                    q.append("virtual")
            except:
                q.append("virtual")
    except: pass
    qprefix = ("[" + ", ".join(q) + "] ") if q else ""
    return "{}{} {}({})".format(qprefix, ret, name, params_str)

def ctor_signature(ctor):
    try:
        pars = [ param_label(p) for p in ctor.GetParameters() ]
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
        if p.CanRead:
            g = p.GetGetMethod(True)
            acc.append("get{}".format(" (static)" if (g and g.IsStatic) else ""))
        if p.CanWrite:
            s = p.GetSetMethod(True)
            acc.append("set{}".format(" (static)" if (s and s.IsStatic) else ""))
    except: pass
    # indexers have parameters (Item[...] in C#)
    idx = ""
    try:
        ip = p.GetIndexParameters()
        if ip and len(ip) > 0:
            idx = "[{}]".format(", ".join(param_label(pp) for pp in ip))
    except: pass
    return "{} {}{} {{ {} }}".format(tname, name, idx, "; ".join(acc) if acc else "")

def field_signature(f):
    name = getattr(f, "Name", "<?>")
    try:
        tname = short_type_name(f.FieldType)
    except:
        tname = "object"
    q = []
    try:
        if f.IsStatic: q.append("static")
        if f.IsInitOnly: q.append("readonly")
        if f.IsLiteral: q.append("const")
    except: pass
    qprefix = ("[" + ", ".join(q) + "] ") if q else ""
    val = None
    try:
        if f.IsLiteral or f.IsStatic:
            val = f.GetRawConstantValue()
    except: pass
    return "{}{} {}".format(qprefix, tname, name) + ((" = {}".format(repr(val))) if val is not None else "")

def event_signature(ev):
    try: handlertype = short_type_name(ev.EventHandlerType)
    except: handlertype = "EventHandler"
    name = getattr(ev, "Name", "<?>")
    return "{} {}".format(handlertype, name)

def obsolete_text(member):
    try:
        for a in member.GetCustomAttributes(True):
            if a.__class__.__name__.endswith("ObsoleteAttribute"):
                msg = getattr(a, "Message", None)
                return msg or "Obsolete"
    except: pass
    return None

def get_flags_binding():
    flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance
    if declared_only:
        flags |= BindingFlags.DeclaredOnly
    return flags

# --- XML doc parsing & lookup ---
def parse_member_id(docid):
    """
    Convert XML doc ID (e.g. 'M:Rhino.Geometry.Point3d.DistanceTo(Rhino.Geometry.Point3d)')
    into (kind, typeFullName, memberName) ignoring parameters.
    """
    if not docid or ":" not in docid: return (None, None, None)
    kind, rest = docid.split(":", 1)
    # split type vs member by last '.'
    # handle nested types: still fine with rsplit
    base = rest
    member = None
    if "(" in rest:
        # method/ctor with params
        head = rest.split("(", 1)[0]
        if "." in head:
            tname, member = head.rsplit(".", 1)
        else:
            tname, member = head, None
    else:
        if "." in rest:
            tname, member = rest.rsplit(".", 1)
        else:
            tname, member = rest, None
    # normalize ctor name
    if kind == "M" and member in ("#ctor", ".ctor"):
        member = "#ctor"
    return (kind, tname, member)

def load_xml_maps_for_assembly(asm):
    """Return dicts: type_summary[typeFull] -> str, member_docs[(typeFull, member, kind)] -> dict."""
    if ET is None:
        return {}, {}
    try:
        loc = asm.Location
    except:
        return {}, {}
    if not loc:
        return {}, {}
    xml_path = os.path.splitext(loc)[0] + ".xml"
    if not os.path.isfile(xml_path):
        return {}, {}
    type_summ = {}
    member_docs = {}
    try:
        root = ET.parse(xml_path).getroot()
        members = root.find("members")
        if members is None:
            return type_summ, member_docs
        for m in members.findall("member"):
            docid = m.get("name") or ""
            kind, tname, mname = parse_member_id(docid)
            if not kind or not tname:
                continue
            summary = (m.findtext("summary") or "").strip()
            remarks = (m.findtext("remarks") or "").strip()
            # collect param docs
            pdocs = {}
            for p in m.findall("param"):
                pname = p.get("name") or ""
                ptext = (p.text or "").strip()
                if pname:
                    pdocs[pname] = ptext
            rtext = (m.findtext("returns") or "").strip()
            obs = None
            # Some XMLs include obsolete info inside <summary>; we keep it as-is.
            entry = {"summary": " ".join(summary.split()),
                     "remarks": " ".join(remarks.split()),
                     "params": pdocs, "returns": rtext}
            if kind == "T":
                if entry["summary"]:
                    type_summ[tname] = entry["summary"]
            else:
                key = (tname, mname or "", kind)
                member_docs[key] = entry
        return type_summ, member_docs
    except Exception:
        return {}, {}

def member_doc_for(xml_member_docs, t_full, name, kind_letter):
    return xml_member_docs.get((t_full, name or "", kind_letter), None)

# --- Assembly selection (robust) ---
def find_assembly(name):
    for a in AppDomain.CurrentDomain.GetAssemblies():
        try:
            if (a.GetName().Name or "") == name:
                return a
        except: pass
    return None

rhino_asm = find_assembly("RhinoCommon")
gh_asm    = find_assembly("Grasshopper")

assemblies = [a for a in (rhino_asm, gh_asm) if a is not None]

if include_plugins:
    for a in AppDomain.CurrentDomain.GetAssemblies():
        try: n = a.GetName().Name or ""
        except: n = ""
        if not n or a in assemblies: continue
        if n.startswith("System") or n.startswith("Microsoft"): continue
        assemblies.append(a)

def asm_version_str(a):
    try:
        v = a.GetName().Version
        return str(v) if v else "unknown"
    except:
        return "unknown"

rhino_version = str(Rhino.RhinoApp.Version)
gh_version    = asm_version_str(gh_asm) if gh_asm else "unknown"

# --- Reflect & collect ---
flags = get_flags_binding()
data = {
    "meta": {
        "generatedAt": datetime.datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S UTC"),
        "python": "CPython {}".format(py_version) if is_cpython else "IronPython {}".format(py_version),
        "rhino": rhino_version,
        "grasshopper": gh_version,
        "declared_only": bool(declared_only),
        "include_plugins": bool(include_plugins),
        "include_components": bool(include_components),
    },
    "assemblies": []
}

# Preload XML summaries + member docs per assembly
asm_xml_type = {}
asm_xml_member = {}
for asm in assemblies:
    tmap, mmap = load_xml_maps_for_assembly(asm) if include_member_xml else ({}, {})
    asm_xml_type[asm] = tmap
    asm_xml_member[asm] = mmap

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

        # type meta
        base_type = None
        try:
            if t.BaseType is not None:
                base_type = short_type_name(t.BaseType)
        except: pass

        interfaces = []
        try:
            for itf in t.GetInterfaces():
                interfaces.append(short_type_name(itf))
        except: pass

        t_entry = {
            "name": t.FullName or t.Name,
            "display": "{}{} {}".format(kind_of_type(t), flag_str(t), t.FullName or t.Name).strip(),
            "summary": asm_xml_type.get(asm, {}).get(t.FullName or "", ""),
            "base": base_type,
            "interfaces": interfaces,
            "nested_types": [],
            "fields": [],
            "constructors": [],
            "properties": [],
            "methods": [],
            "events": []
        }

        # nested types
        try:
            for nt in t.GetNestedTypes(flags):
                if nt.IsPublic or nt.IsNestedPublic:
                    t_entry["nested_types"].append("{} {}".format(kind_of_type(nt), nt.Name))
        except: pass

        # fields (incl. enum members)
        try:
            for f in t.GetFields(flags):
                sig = field_signature(f)
                obs = obsolete_text(f)
                doc = ""
                if include_member_xml:
                    md = member_doc_for(asm_xml_member.get(asm, {}), t.FullName or "", f.Name, "F")
                    if md and md.get("summary"): doc = md["summary"]
                t_entry["fields"].append((sig, doc, obs))
        except: pass

        # ctors
        try:
            for ctor in t.GetConstructors(flags):
                sig = ctor_signature(ctor)
                doc = ""
                if include_member_xml:
                    md = member_doc_for(asm_xml_member.get(asm, {}), t.FullName or "", "#ctor", "M")
                    if md and md.get("summary"): doc = md["summary"]
                t_entry["constructors"].append((sig, doc, None))
        except: pass

        # properties
        try:
            for p in t.GetProperties(flags):
                # keep indexers too (useful)
                sig = property_signature(p)
                obs = obsolete_text(p)
                doc = ""
                if include_member_xml:
                    md = member_doc_for(asm_xml_member.get(asm, {}), t.FullName or "", p.Name, "P")
                    if md and md.get("summary"): doc = md["summary"]
                t_entry["properties"].append((sig, doc, obs))
        except: pass

        # methods
        try:
            for m in t.GetMethods(flags):
                if getattr(m, "IsSpecialName", False):  # skip get_/set_/add_/remove_
                    continue
                sig = method_signature(m)
                obs = obsolete_text(m)
                doc = ""
                pdocs = {}
                rdoc = ""
                if include_member_xml:
                    md = member_doc_for(asm_xml_member.get(asm, {}), t.FullName or "", m.Name, "M")
                    if md:
                        doc  = md.get("summary","")
                        pdocs = md.get("params",{}) or {}
                        rdoc  = md.get("returns","") or ""
                # capture parameter names to attach pdocs later in rendering
                pnames = []
                try:
                    pnames = [p.Name for p in m.GetParameters()]
                except: pass
                t_entry["methods"].append((sig, doc, obs, pnames, pdocs, rdoc))
        except: pass

        # events
        try:
            for ev in t.GetEvents(flags):
                sig = event_signature(ev)
                obs = obsolete_text(ev)
                doc = ""
                if include_member_xml:
                    md = member_doc_for(asm_xml_member.get(asm, {}), t.FullName or "", ev.Name, "E")
                    if md and md.get("summary"): doc = md["summary"]
                t_entry["events"].append((sig, doc, obs))
        except: pass

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

data["py_modules"] = py_modules

# --- Optional: Grasshopper components catalog (heavy) ---
components_catalog = None
if include_components:
    components_catalog = {}
    try:
        server = Grasshopper.Instances.ComponentServer
        # Iterate proxies, instantiate components to read IO metadata
        for proxy in list(server.ObjectProxies):
            comp = None
            try:
                comp = proxy.CreateInstance()
            except Exception:
                comp = None
            # Only GH_Component instances
            try:
                if comp is None: continue
                import Grasshopper.Kernel as ghk
                if not isinstance(comp, ghk.GH_Component):
                    continue
            except Exception:
                continue
            cat = getattr(comp, "Category", "Other") or "Other"
            sub = getattr(comp, "SubCategory", "") or ""
            bucket = components_catalog.setdefault(cat, {}).setdefault(sub, [])
            # IO params
            def dump_params(plist):
                out = []
                try:
                    for p in plist:
                        try:
                            tname = getattr(p, "TypeName", "") or ""
                        except:
                            tname = ""
                        out.append({
                            "name": getattr(p, "Name", "") or "",
                            "nick": getattr(p, "NickName", "") or "",
                            "desc": getattr(p, "Description", "") or "",
                            "type": tname,
                            "optional": getattr(p, "Optional", False),
                            "access": str(getattr(p, "Access", "")),
                        })
                except Exception:
                    pass
                return out
            bucket.append({
                "name": getattr(comp, "Name", ""),
                "nick": getattr(comp, "NickName", ""),
                "desc": getattr(comp, "Description", ""),
                "guid": str(comp.ComponentGuid),
                "inputs": dump_params(getattr(comp.Params, "Input", [])),
                "outputs": dump_params(getattr(comp.Params, "Output", [])),
            })
            try:
                comp.Dispose()
            except Exception:
                pass
    except Exception:
        components_catalog = None

# --- Render Markdown ---
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
    "- Include GH components catalog: {}".format(meta["include_components"]),
    "",
    "---",
    ""
]

# Assemblies → Namespaces → Types
for asm in data["assemblies"]:
    lines.append("## Assembly `{}` ({})".format(asm["name"], asm["version"]))
    for ns in sorted(asm["namespaces"].keys()):
        lines.append("")
        lines.append("### Namespace `{}`".format(ns))
        types = sorted(data["assemblies"][data["assemblies"].index(asm)]["namespaces"][ns], key=lambda x: x["name"])
        for t in types:
            lines.append("")
            lines.append("#### {}".format(md_escape(t["display"])))
            if t.get("base"):
                lines.append("- **Base type:** `{}`".format(md_escape(t["base"])))
            if t.get("interfaces"):
                lines.append("- **Implements:** " + ", ".join("`{}`".format(md_escape(i)) for i in t["interfaces"]))
            if t.get("summary"):
                lines.append("")
                lines.append("> " + md_escape(t["summary"]))
            if t.get("nested_types"):
                lines.append("")
                lines.append("**Nested Types**")
                for nt in t["nested_types"]:
                    lines.append("- `{}`".format(md_escape(nt)))

            # Fields (incl enums)
            if t["fields"]:
                lines.append("")
                lines.append("**Fields**")
                for sig, doc, obs in t["fields"]:
                    extra = []
                    if obs: extra.append("_Obsolete: {}_".format(md_escape(obs)))
                    if doc: extra.append(md_escape(doc))
                    lines.append("- `{}`{}".format(md_escape(sig), (": " + " ".join(extra)) if extra else ""))

            # Constructors
            if t["constructors"]:
                lines.append("")
                lines.append("**Constructors**")
                for sig, doc, obs in t["constructors"]:
                    extra = []
                    if obs: extra.append("_Obsolete: {}_".format(md_escape(obs)))
                    if doc: extra.append(md_escape(doc))
                    lines.append("- `{}`{}".format(md_escape(sig), (": " + " ".join(extra)) if extra else ""))

            # Properties
            if t["properties"]:
                lines.append("")
                lines.append("**Properties**")
                for sig, doc, obs in t["properties"]:
                    extra = []
                    if obs: extra.append("_Obsolete: {}_".format(md_escape(obs)))
                    if doc: extra.append(md_escape(doc))
                    lines.append("- `{}`{}".format(md_escape(sig), (": " + " ".join(extra)) if extra else ""))

            # Methods
            if t["methods"]:
                lines.append("")
                lines.append("**Methods**")
                for sig, doc, obs, pnames, pdocs, rdoc in t["methods"]:
                    detail = []
                    if obs: detail.append("_Obsolete: {}_".format(md_escape(obs)))
                    if doc: detail.append(md_escape(doc))
                    if pnames and pdocs:
                        parms = []
                        for pn in pnames:
                            if pn in pdocs and pdocs[pn]:
                                parms.append("`{}`: {}".format(md_escape(pn), md_escape(pdocs[pn])))
                        if parms:
                            detail.append("**Parameters:** " + "; ".join(parms))
                    if rdoc:
                        detail.append("**Returns:** {}".format(md_escape(rdoc)))
                    lines.append("- `{}`{}".format(md_escape(sig), (": " + " ".join(detail)) if detail else ""))

            # Events
            if t["events"]:
                lines.append("")
                lines.append("**Events**")
                for sig, doc, obs in t["events"]:
                    extra = []
                    if obs: extra.append("_Obsolete: {}_".format(md_escape(obs)))
                    if doc: extra.append(md_escape(doc))
                    lines.append("- `{}`{}".format(md_escape(sig), (": " + " ".join(extra)) if extra else ""))

    lines.append("")
    lines.append("---")
    lines.append("")

# Python modules
lines.append("## Python Modules")
for mod in data.get("py_modules", []):
    lines.append("")
    lines.append("### {}".format(mod["name"]))
    for f in sorted(mod["functions"], key=lambda it: it["name"].lower()):
        sig = f["signature"] or ""
        doc = f.get("doc","") or ""
        if doc:
            lines.append("- **{}** `{}` — {}".format(f["name"], md_escape(sig), md_escape(" ".join(doc.split()))))
        else:
            lines.append("- **{}** `{}`".format(f["name"], md_escape(sig)))
    lines.append("")

# Optional GH components catalog
if components_catalog:
    lines.append("---")
    lines.append("")
    lines.append("## Grasshopper Components (Loaded Catalog)")
    for cat in sorted(components_catalog.keys()):
        lines.append("")
        lines.append(f"### {cat}")
        subs = components_catalog[cat]
        for sub in sorted(subs.keys()):
            lines.append("")
            lines.append(f"#### {sub or '(no subcategory)'}")
            for comp in sorted(subs[sub], key=lambda c: (c["name"] or "").lower()):
                lines.append("")
                lines.append(f"**{comp['name']}** (*{comp['nick']}*) — `{comp['guid']}`")
                if comp.get("desc"):
                    lines.append("> " + md_escape(comp["desc"]))
                if comp.get("inputs"):
                    lines.append("- **Inputs:**")
                    for p in comp["inputs"]:
                        lines.append("  - `{}` (*{}*) — {}{}{}".format(
                            md_escape(p["nick"] or p["name"]),
                            md_escape(p.get("type","")),
                            md_escape(p.get("desc","")),
                            " — optional" if p.get("optional") else "",
                            " — access: {}".format(p.get("access","")) if p.get("access") else ""
                        ))
                if comp.get("outputs"):
                    lines.append("- **Outputs:**")
                    for p in comp["outputs"]:
                        lines.append("  - `{}` (*{}*) — {}".format(
                            md_escape(p["nick"] or p["name"]),
                            md_escape(p.get("type","")),
                            md_escape(p.get("desc",""))
                        ))

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

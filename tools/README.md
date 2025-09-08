# Tools - AI Development Utilities

Python utilities for AI-powered project analysis, dumping, and reconstruction. Perfect for feeding entire projects to ChatGPT, Claude, or other language models! 

All commands should be run from this directory (`tools/`):

## 📁 Contents

| File | Description | Usage |
|------|-------------|-------|
| 🗄️ `dump.py` | Project structure dumper | Exports entire codebase to text format |
| 🔄 `response.py` | Project reconstructor | Rebuilds project from AI responses |
| 📄 `dump.txt` | Generated dump file | Contains flattened project structure |
| 💬 `response.txt` | AI response file | Reconstructed project from AI output |
| 🔮 `gemini-system-prompt.md` | Gemini system prompt | Ensures consistent AI response formatting |

## 🚀 Quick Start

### 1. 📤 Dump Your Project
```bash
python dump.py
```
This creates a `dump.txt` file containing your entire project structure in a format perfect for AI consumption!

### 2. 🧠 Feed to AI
Copy the contents of `dump.txt` and paste into your favorite AI chat (ChatGPT, Claude, etc.)

> 💡 **Pro Tip**: For Gemini users, check out `gemini-system-prompt.md` for a custom system prompt that ensures perfect response formatting!

### 3. 📥 Reconstruct from AI Response  
```bash
python response.py
```
This rebuilds your project from the AI's response saved in `response.txt`!

## 🎯 Use Cases

### 🔍 **Code Analysis**
```
"Analyze this entire Grasshopper project for performance issues"
[paste dump.txt contents]
```

### 🛠️ **Refactoring**
```
"Refactor this codebase to use modern C# patterns"
[paste dump.txt contents]
```

### 🎨 **Code Modernization**
```
"Update this project to use latest .NET practices and async/await patterns"
[paste dump.txt contents]
```

### 🐛 **Bug Hunting**
```
"Find all potential security vulnerabilities in this project"
[paste dump.txt contents]
```

### 📚 **Documentation**
```
"Generate comprehensive documentation for this codebase"
[paste dump.txt contents]
```

## ⚙️ Configuration

### 🚫 Excluded Folders
The dumper automatically skips these common folders:
- `node_modules` 📦
- `venv`, `.venv` 🐍  
- `__pycache__` 🐍
- `dist`, `build` 🏗️
- `bin`, `obj` (C# build outputs)
- `.git`, `.gpt`, `tools` 🤖

### 📄 Supported File Types
Includes all common development files:
- **C#**: `.cs`, `.csproj`, `.sln`
- **Frontend**: `.js`, `.jsx`, `.ts`, `.tsx`, `.vue`, `.svelte`
- **Styles**: `.css`, `.scss`, `.sass`, `.less`
- **Config**: `.json`, `.yaml`, `.yml`, `.ini`, `.env`
- **Docs**: `.md`, `.txt`, `.log`
- **Python**: `.py`, `.ipynb`
- **And many more!**

## 🔄 Workflow Example

```bash
# 1. Dump your current project
python tools/dump.py

# 2. Open dump.txt and copy contents
cat tools/dump.txt

# 3. Paste into AI chat with your request
# "Please modernize the C# code and add proper error handling"

# 4. Save AI response to response.txt
# (Copy AI's code output to tools/response.txt)

# 5. Reconstruct the improved project
python tools/response.py
```

## 📋 File Format

### Dump Format (`dump.txt`)
```
<./grasshopper-plugin/LiveCodingGH/Component.cs>
using System;
// ... file contents ...

<./mcp-server/src/tools/canvas.js>  
export async function getCanvasState() {
// ... file contents ...
}

&&& FILE: ./assets/logo.png
&&& ERROR: Binary file - content not included
```

### Response Format (`response.txt`)
Same format! AI responses should maintain the `<./path/to/file>` structure for automatic reconstruction.

> 🔮 **Gemini Integration**: The included `gemini-system-prompt.md` contains a system prompt that guarantees Gemini outputs in this exact format, making reconstruction seamless!

## 🎨 Tips & Tricks

### 💡 **Better AI Prompts**
- Be specific about what you want changed
- Ask for explanations of modifications
- Request the same file format for easy reconstruction
- 🔮 **For Gemini**: Use the provided system prompt for guaranteed compatibility!

### 🔧 **Customization** 
- Edit `allowed_extensions` in `dump.py` for different file types
- Modify `exclude_folders` to skip additional directories
- Adjust the reconstruction logic in `response.py`

### 🚀 **Productivity Boost**
- Use for rapid prototyping with AI assistance
- Great for learning new patterns across codebases  
- Perfect for migration and modernization projects
- Excellent for code reviews and architecture analysis

### 🤖 **AI-Specific Tips**

**For ChatGPT:**
- Break large dumps into smaller chunks if you hit token limits
- Ask for step-by-step explanations of changes
- Request test cases for modified code

**For Claude:**
- Use Claude's excellent code analysis capabilities
- Ask for architecture improvements and patterns
- Request performance optimizations

**For Gemini:**
- Use the included system prompt for consistent formatting
- Great for multi-language projects
- Ask for comprehensive refactoring suggestions

## Integration with Monorepo

Since this is part of a monorepo structure:

### Dump Specific Components
```bash
# Dump only the C# plugin
python dump.py --path ../grasshopper-plugin

# Dump only the MCP server
python dump.py --path ../mcp-server
```

### Component-Specific Analysis
```
"Analyze only the MCP server component for Node.js best practices"
[paste component-specific dump]
```

### Cross-Component Refactoring
```
"Ensure consistent error handling patterns between the C# plugin and Node.js server"
[paste full project dump]
```

## 🛡️ Safety Notes

- ⚠️ Always backup your project before reconstruction
- 🔍 Review AI changes before applying them
- 🧪 Test thoroughly after reconstruction
- 📁 Keep original files until you're satisfied with results
- 🔒 Never include sensitive data (API keys, passwords) in dumps

## Advanced Usage

### Custom Dump Scripts

Create specialized dumpers for specific use cases:

```python
# dump_only_interfaces.py
# Only dump interface definitions and public APIs
```

### Automated Workflows

```bash
# Create a development workflow
python dump.py && echo "Project dumped, ready for AI analysis"
```

### Integration with CI/CD

Use these tools in automated workflows to:
- Generate documentation
- Analyze code quality
- Suggest improvements
- Create migration guides

---

*Happy coding with AI! 🎉*
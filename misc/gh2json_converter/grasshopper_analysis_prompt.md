# 🔬 Grasshopper Algorithm Analysis Prompt

You are analyzing a Python pseudocode file generated from a Grasshopper definition. Create a technical specification that describes the algorithm like a scientific paper.

## 📋 **Analysis Requirements:**

### **1. High-Level Overview**
Start with a precise paragraph describing:
- What this algorithm does (purpose and function)
- What inputs it requires
- What outputs it produces
- The overall computational approach

### **2. Algorithm Description**
Write a scientific-style description of how the algorithm works:
- **Reference actual component names** using backticks (e.g., `livingroom_1c67`, `furnisher_fda3_data`)
- **Follow logical algorithm flow**, NOT pseudocode chronological order
- **Group related operations** even if they appear in different sections
- **Explain the mathematical/geometric operations** being performed

### **3. Key Processing Stages**
Identify and describe the main computational phases:
- Input processing and geometry preparation
- Core algorithmic operations
- Optimization or iteration loops
- Output generation and formatting

### **4. Important Parameters & Thresholds**
List significant numerical values and their purposes:
- Extract actual values from the pseudocode
- Explain their role in the algorithm

### **5. Uncertainty Handling**
When you encounter components whose purpose is unclear:
- Mark as **"*Suggested interpretation:*"**
- Provide your best guess but clearly indicate uncertainty
- Never invent functionality that isn't evident

## ⚠️ **What NOT to Include:**

- **No business value estimates** (time savings, production readiness, etc.)
- **No invented explanations** - only describe what you can clearly identify
- **No chronological pseudocode walkthrough** - focus on algorithm logic

## 📝 **Writing Style:**
- Scientific paper tone (formal, precise, technical)
- Use present tense for algorithm description
- Include actual component variable names throughout the text
- Structure explanations by algorithm logic, not code order
- **Maximum 5000 characters total** - prioritize most important algorithm aspects

## 🎯 **Example Structure:**

```markdown
# 🔬 Algorithm Analysis: [Purpose]

## 📋 Overview
This algorithm [precise description of function, inputs, outputs, approach]...

## ⚙️ Core Algorithm
The system begins by processing input geometries through `component_name_uuid`...
[Describe logical flow with component references]

## 🔄 Processing Stages
### 🎯 Stage 1: [Logical grouping]
Components `x_1234` and `y_5678` work together to...

### 🔧 Stage 2: [Next logical step]
*Suggested interpretation:* The `unclear_component_abcd` appears to...

## 📊 Key Parameters
- `parameter_value`: 0.45 - controls door width spacing
- `threshold_xyz`: 20 - maximum optimization score

## 📤 Algorithm Output
The final results are generated through `final_component_uuid`...
```

**Analyze the provided pseudocode file following these guidelines.**
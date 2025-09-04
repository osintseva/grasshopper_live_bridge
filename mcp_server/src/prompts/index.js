import { getLogger } from '../utils/logger.js';
import { getCanvasCache } from '../state/canvas-cache.js';

const logger = getLogger();

export async function getAvailablePrompts() {
  return [
    {
      name: "analyze_canvas",
      description: "Analyze the current Grasshopper canvas and provide insights",
      arguments: [
        {
          name: "focus",
          description: "What to focus the analysis on (performance, structure, errors)",
          required: false
        }
      ]
    },
    {
      name: "debug_component",
      description: "Help debug a specific component with errors",
      arguments: [
        {
          name: "componentUuid",
          description: "UUID of the component to debug",
          required: true
        }
      ]
    }
  ];
}

export async function getPrompt(name, args = {}) {
  logger.toolCall('getPrompt', { name, args });
  
  switch (name) {
    case "analyze_canvas":
      return await generateCanvasAnalysisPrompt(args);
    
    case "debug_component":
      return await generateDebugPrompt(args);
    
    default:
      throw new Error(`Unknown prompt: ${name}`);
  }
}

async function generateCanvasAnalysisPrompt(args) {
  const cache = getCanvasCache();
  const canvas = await cache.getCanvas();
  
  const focus = args.focus || "general";
  const componentCount = canvas.Components?.length || 0;
  const connectionCount = canvas.Connections?.length || 0;
  
  const prompt = `Please analyze this Grasshopper definition:

**Stats:**
- Components: ${componentCount}
- Connections: ${connectionCount}
- Analysis Focus: ${focus}

**Key Areas to Review:**
${focus === 'performance' ? '- Performance bottlenecks and optimization opportunities' : ''}
${focus === 'structure' ? '- Definition organization and clarity' : ''}
${focus === 'errors' ? '- Error analysis and resolution suggestions' : ''}
${focus === 'general' ? '- Overall structure, potential issues, and improvements' : ''}

**Canvas Data:**
\`\`\`json
${JSON.stringify(canvas, null, 2)}
\`\`\`

Provide specific, actionable insights about this Grasshopper definition.`;

  return {
    description: `Canvas analysis with ${focus} focus`,
    messages: [
      {
        role: "user",
        content: {
          type: "text",
          text: prompt
        }
      }
    ]
  };
}

async function generateDebugPrompt(args) {
  const { componentUuid } = args;
  
  if (!componentUuid) {
    throw new Error('componentUuid is required for debug_component prompt');
  }
  
  const cache = getCanvasCache();
  const canvas = await cache.getCanvas();
  const component = canvas.Components?.find(c => c.InstanceGuid === componentUuid);
  
  if (!component) {
    throw new Error(`Component not found: ${componentUuid}`);
  }
  
  const errors = component.RuntimeMessages || [];
  const connections = canvas.Connections?.filter(c => 
    c.SourceId === componentUuid || c.TargetId === componentUuid
  ) || [];
  
  const prompt = `Help debug this Grasshopper component:

**Component Details:**
- Name: ${component.Name}
- Nickname: ${component.NickName || 'None'}
- Type: ${component.TypeName || 'Unknown'}
- Has Errors: ${errors.length > 0 ? 'Yes' : 'No'}

**Error Messages:**
${errors.length > 0 ? errors.map(e => `- ${e.Level}: ${e.Text}`).join('\n') : 'No errors found'}

**Connections:**
- Inputs: ${component.Inputs?.length || 0}
- Outputs: ${component.Outputs?.length || 0}
- Total Connections: ${connections.length}

**Component Data:**
\`\`\`json
${JSON.stringify(component, null, 2)}
\`\`\`

**Related Connections:**
\`\`\`json
${JSON.stringify(connections, null, 2)}
\`\`\`

Please help diagnose and fix any issues with this component.`;

  return {
    description: `Debug component: ${component.Name} (${component.NickName || componentUuid})`,
    messages: [
      {
        role: "user",
        content: {
          type: "text", 
          text: prompt
        }
      }
    ]
  };
}
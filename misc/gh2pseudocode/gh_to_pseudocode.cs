/*
 * HOW TO USE THIS SCRIPT:
 * 1. Open Grasshopper in Rhino 8.
 * 2. Add a "C# Script" component to your canvas (found under Math > Script).
 * 3. Zoom in on the C# component until a black bar with "..." appears. Click it to open the script editor.
 * 4. In the script editor, replace all the existing code with the code below.
 * 5. Click "OK" to compile and close the editor.
 * 6. The component will have one output, 'A', which will contain the Python-like pseudocode string
 * representing the entire Grasshopper definition.
 * 7. Connect a "Panel" component to the 'A' output to view the pseudocode.
 *
 * This script requires no inputs. It automatically analyzes the document it belongs to.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

public class Script_Instance : GH_ScriptInstance
{
	private const bool INCLUDE_DATA_PREVIEWS = false; // Set to false to disable inline comments with data previews
	private const int DATA_PREVIEW_LIMIT = 100;
	private const int UUID_LENGTH = 8; // First 8 chars of UUID

	/// <summary>
	/// Generates a compact data preview string (max 100 chars).
	/// </summary>
	private string GetCompactDataPreview(IGH_Goo goo)
	{
		if (goo == null) return "null";

		string preview = "";
		switch (goo.ScriptVariable())
		{
			case Point3d pt:
				preview = $"Pt({pt.X:F2},{pt.Y:F2},{pt.Z:F2})";
				break;

			case Line line:
				preview = $"Line[({line.From.X:F2},{line.From.Y:F2},{line.From.Z:F2})->({line.To.X:F2},{line.To.Y:F2},{line.To.Z:F2})]";
				break;

			case Curve curve when curve.IsLinear():
				preview = $"Line[({curve.PointAtStart.X:F2},{curve.PointAtStart.Y:F2},{curve.PointAtStart.Z:F2})->({curve.PointAtEnd.X:F2},{curve.PointAtEnd.Y:F2},{curve.PointAtEnd.Z:F2})]";
				break;

			case Curve curve:
				var nurbsCurve = curve.ToNurbsCurve();
				if (nurbsCurve != null)
				{
					string pointsStr = string.Join(";", nurbsCurve.Points.Select(p => $"({p.Location.X:F2},{p.Location.Y:F2},{p.Location.Z:F2})"));
					preview = $"CurvePts[{pointsStr}]";
				}
				else
				{
					preview = $"Curve(L={curve.GetLength():F2})";
				}
				break;

			case Brep brep:
				preview = $"Brep(V={brep.Vertices.Count},F={brep.Faces.Count})";
				break;

			default:
				preview = goo.ToString();
				break;
		}

		// Truncate to 100 characters
		if (preview.Length > DATA_PREVIEW_LIMIT)
		{
			preview = preview.Substring(0, DATA_PREVIEW_LIMIT - 3) + "...";
		}

		return preview;
	}

	/// <summary>
	/// Truncates UUID to specified length and handles collisions.
	/// </summary>
	private string GetShortUuid(Guid guid, HashSet<string> usedUuids)
	{
		string uuidStr = guid.ToString("N"); // No hyphens
		for (int len = 4; len <= UUID_LENGTH; len += 2)
		{
			string shortUuid = uuidStr.Substring(0, len);
			if (!usedUuids.Contains(shortUuid))
			{
				usedUuids.Add(shortUuid);
				return shortUuid;
			}
		}
		// Fallback if all lengths are taken (very unlikely)
		usedUuids.Add(uuidStr.Substring(0, UUID_LENGTH));
		return uuidStr.Substring(0, UUID_LENGTH);
	}

	/// <summary>
	/// Generates a meaningful variable name from component info.
	/// </summary>
	private string GenerateVariableName(PseudocodeComponent comp, HashSet<string> usedUuids)
	{
		string baseName = "";

		// Priority: NickName > Name > ComponentType
		if (!string.IsNullOrEmpty(comp.NickName))
		{
			baseName = comp.NickName.ToLower().Replace(" ", "_").Replace("-", "_");
		}
		else if (!string.IsNullOrEmpty(comp.Name))
		{
			baseName = comp.Name.ToLower().Replace(" ", "_").Replace("-", "_");
		}
		else
		{
			baseName = "component";
		}

		// Remove invalid characters for Python variable names
		baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"[^a-z0-9_]", "");
		if (string.IsNullOrEmpty(baseName) || char.IsDigit(baseName[0]))
		{
			baseName = "comp_" + baseName;
		}

		string shortUuid = GetShortUuid(comp.Guid, usedUuids);
		return $"{baseName}_{shortUuid}";
	}

	/// <summary>
	/// Performs topological sort on components based on their dependencies.
	/// </summary>
	private List<PseudocodeComponent> TopologicalSort(List<PseudocodeComponent> components)
	{
		var sorted = new List<PseudocodeComponent>();
		var visited = new HashSet<int>();
		var visiting = new HashSet<int>();
		var compById = components.ToDictionary(c => c.Id, c => c);

		void Visit(int compId)
		{
			if (visiting.Contains(compId))
			{
				// Cycle detected - just add it anyway
				return;
			}
			if (visited.Contains(compId)) return;

			visiting.Add(compId);

			if (compById.TryGetValue(compId, out var comp))
			{
				// Visit all dependencies first
				foreach (var input in comp.Inputs)
				{
					foreach (var connection in input.Connections)
					{
						Visit(connection[0]); // Visit source component
					}
				}

				visited.Add(compId);
				sorted.Add(comp);
			}

			visiting.Remove(compId);
		}

		// Visit all components
		foreach (var comp in components)
		{
			if (!visited.Contains(comp.Id))
			{
				Visit(comp.Id);
			}
		}

		return sorted;
	}

	private void RunScript(ref object A)
	{
		var doc = this.Component.OnPingDocument();
		if (doc == null)
		{
			AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Cannot access the Grasshopper document.");
			return;
		}

		var components = new List<PseudocodeComponent>();
		var docObjects = doc.Objects.ToList();
		var guidToIndexMap = docObjects.Select((obj, i) => new { obj.InstanceGuid, i }).ToDictionary(x => x.InstanceGuid, x => x.i);
		var usedUuids = new HashSet<string>();

		// Parse all components
		for (int i = 0; i < docObjects.Count; i++)
		{
			var obj = docObjects[i];
			var comp = new PseudocodeComponent
			{
				Id = i,
				Guid = obj.InstanceGuid
			};

			if (obj is IGH_Component ghComponent)
			{
				comp.Name = ghComponent.Name;
				comp.NickName = ghComponent.NickName;
				comp.Description = ghComponent.Description;
				comp.IsComponent = true;

				// Parse inputs
				foreach (var inputParam in ghComponent.Params.Input)
				{
					var connections = new List<int[]>();
					foreach (var source in inputParam.Sources)
					{
						var sourceOwnerObj = source.Attributes.GetTopLevel.DocObject;
						if (sourceOwnerObj != null && guidToIndexMap.TryGetValue(sourceOwnerObj.InstanceGuid, out int sourceId))
						{
							int sourceParamIndex = (sourceOwnerObj is IGH_Component sourceComp) ? sourceComp.Params.Output.IndexOf(source) : 0;
							if (sourceParamIndex > -1) connections.Add(new int[] { sourceId, sourceParamIndex });
						}
					}
					comp.Inputs.Add(new PseudocodeInput { Name = inputParam.Name, TypeName = inputParam.TypeName, Connections = connections });
				}

				// Parse outputs
				comp.Outputs = ghComponent.Params.Output.Select(p => {
					var data = p.VolatileData.AllData(true);
					var dataPreview = data.Any() ? string.Join(", ", data.Select(GetCompactDataPreview)) : "null";

					return new PseudocodeOutput { Name = p.Name, TypeName = p.TypeName, DataPreview = dataPreview, HasRecipients = p.Recipients.Count > 0 };
				}).ToList();
			}
			else if (obj is IGH_Param ghParam)
			{
				comp.Name = ghParam.Name;
				comp.NickName = ghParam.NickName;
				comp.Description = ghParam.Description;
				comp.IsComponent = false;

				var data = ghParam.VolatileData.AllData(true);
				var dataPreview = data.Any() ? string.Join(", ", data.Select(GetCompactDataPreview)) : "null";

				comp.Outputs.Add(new PseudocodeOutput { Name = ghParam.Name, TypeName = ghParam.TypeName, DataPreview = dataPreview, HasRecipients = ghParam.Recipients.Count > 0 });

				if (ghParam.Sources.Any())
				{
					var connections = new List<int[]>();
					foreach (var source in ghParam.Sources)
					{
						var sourceOwnerObj = source.Attributes.GetTopLevel.DocObject;
						if (sourceOwnerObj != null && guidToIndexMap.TryGetValue(sourceOwnerObj.InstanceGuid, out int sourceId))
						{
							int sourceParamIndex = (sourceOwnerObj is IGH_Component sourceComp) ? sourceComp.Params.Output.IndexOf(source) : 0;
							if (sourceParamIndex > -1) connections.Add(new int[] { sourceId, sourceParamIndex });
						}
					}
					comp.Inputs.Add(new PseudocodeInput { Name = "Input", TypeName = ghParam.TypeName, Connections = connections });
				}
			}

			components.Add(comp);
		}

		// Generate variable names for all components
		foreach (var comp in components)
		{
			comp.VariableName = GenerateVariableName(comp, usedUuids);
		}

		// Sort components topologically
		var sortedComponents = TopologicalSort(components);

		// Generate pseudocode
		var output = new StringBuilder();

		// Header
		output.AppendLine("# === GRASSHOPPER DEFINITION PSEUDOCODE ===");
		output.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

		var sourceCount = sortedComponents.Count(c => !c.Inputs.Any());
		var sinkCount = sortedComponents.Count(c => !c.Outputs.Any(o => o.HasRecipients));
		var totalConnections = sortedComponents.Sum(c => c.Inputs.Sum(i => i.Connections.Count));

		output.AppendLine($"# Components: {sortedComponents.Count} | Connections: {totalConnections} | Sources: {sourceCount} | Sinks: {sinkCount}");
		output.AppendLine();

		// Source components (no inputs)
		var sources = sortedComponents.Where(c => !c.Inputs.Any()).ToList();
		if (sources.Any())
		{
			output.AppendLine("# === SOURCE DATA (No inputs) ===");
			foreach (var comp in sources)
			{
				var mainOutput = comp.Outputs.FirstOrDefault();
				if (mainOutput != null)
				{
					string typeName = mainOutput.TypeName ?? "Object";
					string preview = INCLUDE_DATA_PREVIEWS ? $"  # {mainOutput.DataPreview}" : "";
					output.AppendLine($"{comp.VariableName}: {typeName} = {comp.Name ?? "Component"}(){preview}");
				}
			}
			output.AppendLine();
		}

		// Main processing chain (components with inputs and outputs)
		var processors = sortedComponents.Where(c => c.Inputs.Any() && c.Outputs.Any(o => o.HasRecipients)).ToList();
		if (processors.Any())
		{
			output.AppendLine("# === MAIN PROCESSING CHAIN ===");
			foreach (var comp in processors)
			{
				var mainOutput = comp.Outputs.FirstOrDefault();
				if (mainOutput != null)
				{
					string typeName = mainOutput.TypeName ?? "Object";
					if (comp.Outputs.Count > 1)
						typeName = $"Tuple[{string.Join(", ", comp.Outputs.Select(o => o.TypeName ?? "Object"))}]";

					// Generate function call
					var functionName = comp.Name ?? "Process";
					var args = new List<string>();

					foreach (var input in comp.Inputs)
					{
						foreach (var connection in input.Connections)
						{
							var sourceComp = sortedComponents.FirstOrDefault(c => c.Id == connection[0]);
							if (sourceComp != null)
							{
								args.Add(sourceComp.VariableName);
							}
						}
					}

					if (comp.Outputs.Count == 1)
					{
						string preview = INCLUDE_DATA_PREVIEWS ? $"  # {mainOutput.DataPreview}" : "";
						output.AppendLine($"{comp.VariableName}: {typeName} = {functionName}({string.Join(", ", args)}){preview}");
					}
					else
					{
						// Multiple outputs
						var outputNames = string.Join(", ", comp.Outputs.Select(o => $"{comp.VariableName}_{o.Name?.ToLower() ?? "out"}"));
						output.AppendLine($"{outputNames}: {typeName} = {functionName}({string.Join(", ", args)})");
					}
				}
			}
			output.AppendLine();
		}

		// Disconnected/Display components (sinks)
		var sinks = sortedComponents.Where(c => c.Outputs.Any() && !c.Outputs.Any(o => o.HasRecipients) && c.Inputs.Any()).ToList();
		if (sinks.Any())
		{
			output.AppendLine("# === DISCONNECTED/DISPLAY COMPONENTS ===");
			foreach (var comp in sinks)
			{
				var mainOutput = comp.Outputs.FirstOrDefault();
				if (mainOutput != null)
				{
					string typeName = mainOutput.TypeName ?? "Object";
					var functionName = comp.Name ?? "Display";
					var args = new List<string>();

					foreach (var input in comp.Inputs)
					{
						foreach (var connection in input.Connections)
						{
							var sourceComp = sortedComponents.FirstOrDefault(c => c.Id == connection[0]);
							if (sourceComp != null)
							{
								args.Add(sourceComp.VariableName);
							}
						}
					}

					string preview = INCLUDE_DATA_PREVIEWS ? $"  # {mainOutput.DataPreview}" : "";
					output.AppendLine($"{comp.VariableName}: {typeName} = {functionName}({string.Join(", ", args)}){preview}");
				}
			}
		}

		A = output.ToString();
	}

	// <Custom additional code>
	public class PseudocodeComponent
	{
		public int Id { get; set; }
		public Guid Guid { get; set; }
		public string Name { get; set; }
		public string NickName { get; set; }
		public string Description { get; set; }
		public bool IsComponent { get; set; }
		public string VariableName { get; set; }
		public List<PseudocodeInput> Inputs { get; set; } = new List<PseudocodeInput>();
		public List<PseudocodeOutput> Outputs { get; set; } = new List<PseudocodeOutput>();
	}

	public class PseudocodeInput
	{
		public string Name { get; set; }
		public string TypeName { get; set; }
		public List<int[]> Connections { get; set; } = new List<int[]>();
	}

	public class PseudocodeOutput
	{
		public string Name { get; set; }
		public string TypeName { get; set; }
		public string DataPreview { get; set; }
		public bool HasRecipients { get; set; }
	}
	// </Custom additional code>
}
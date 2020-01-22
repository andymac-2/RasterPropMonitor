using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using System.Reflection;

namespace JSI
{
	static class SCANsat
	{
		static bool ranPatch = false;

		public static void PatchMaterial()
		{
			if (ranPatch) return;
			ranPatch = true;

			try
			{
				var scansatAssembly = AssemblyLoader.loadedAssemblies.FirstOrDefault(assembly => assembly.name == "SCANsat");

				// no scansat, nothing to do
				if (scansatAssembly == null) return;

				var jutil_t = scansatAssembly.assembly.GetExportedTypes().SingleOrDefault(t => t.FullName == "SCANsat.JUtil");

				// can't find the type? weird...
				if (jutil_t == null)
				{
					JUtil.LogErrorMessage("scansat", "no jutil type found");
					return;
				}

				var lineMatFieldInfo = jutil_t.GetField("LineMat");

				if (lineMatFieldInfo == null)
				{
					JUtil.LogErrorMessage("scansat", "unable to find LineMat field info; valid fields are {0}",
						string.Join(", ", jutil_t.GetFields().Select(field => field.Name)));
					return;
				}

				var lineMaterial = new Material(Shader.Find("KSP/Particles/Alpha Blended"));
				lineMaterial.hideFlags = HideFlags.HideAndDontSave;
				lineMaterial.shader.hideFlags = HideFlags.HideAndDontSave;

				lineMatFieldInfo.SetValue(null, lineMaterial);
				JUtil.LogMessage("scansat", "patched linematerial");
			}
			catch(Exception e)
			{
				JUtil.LogErrorMessage("scansat", e.Message);
			}
		}
	}
}

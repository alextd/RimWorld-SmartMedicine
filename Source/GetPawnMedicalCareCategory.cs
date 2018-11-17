using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using Verse;
using RimWorld;
using Harmony;

namespace SmartMedicine
{
	[StaticConstructorOnStartup]
	static class GetPawnMedicalCareCategory
	{
		static MethodInfo pharmacistTendAdvice;
		static GetPawnMedicalCareCategory()
		{
			Log.Message($"Trying to find Pharmacist");
			Type pharmacist = AccessTools.TypeByName("Pharmacist.PharmacistUtility");
			if (pharmacist == null) return;

			pharmacistTendAdvice = AccessTools.Method(pharmacist, "TendAdvice", new Type[] { typeof(Pawn)});
			Log.Message($"Pharmacist type is {pharmacist}, advice is {pharmacistTendAdvice}");
		}

		public static MedicalCareCategory GetCare(this Pawn pawn)
		{
			MedicalCareCategory care;
			if (pharmacistTendAdvice != null)
			{
				care = (MedicalCareCategory)pharmacistTendAdvice.Invoke(null, new object[] { pawn });
				Log.Message($"Pharmacist tend advicefor {pawn} is {care}");
			}
			else
			{
				care = pawn.playerSettings?.medCare ?? MedicalCareCategory.NoCare;
				Log.Message($"Care for {pawn} is {care}");
			}
			return care;
		}
	}
}

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
			Type pharmacist = AccessTools.TypeByName("Pharmacist.PharmacistUtility");
			if (pharmacist == null) return;

			pharmacistTendAdvice = AccessTools.Method(pharmacist, "TendAdvice");
		}

		public static MedicalCareCategory GetCare(this Pawn pawn)
		{
			if (pharmacistTendAdvice != null)
				return (MedicalCareCategory)pharmacistTendAdvice.Invoke(pawn, new object[] { });

			return  pawn.playerSettings?.medCare ?? MedicalCareCategory.NoCare;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using Verse;
using RimWorld;
using HarmonyLib;

namespace SmartMedicine
{
	[StaticConstructorOnStartup]
	static class GetPawnMedicalCareCategory
	{
		//        public static MedicalCareCategory TendAdvice(Pawn patient) 
		public delegate MedicalCareCategory TendAdviceDel(Pawn patient);
		public static TendAdviceDel TendAdvice;

		static GetPawnMedicalCareCategory()
		{
			try
			{
				//Harmony 2.2.1
				Type pharmacist = AccessTools.TypeByName("Pharmacist.PharmacistUtility");
				if (pharmacist == null) return;

				TendAdvice = AccessTools.MethodDelegate<TendAdviceDel>(AccessTools.Method(pharmacist, "TendAdvice", new Type[] { typeof(Pawn) }));
				Log.Message("Got Pharmacist");
			}
			catch (Exception)
			{ //Well you dont have pharmacist then}
				Verse.Log.Warning("Smart Medicine couldn't find Pharmacist TendAdvice?");
			}
		}

		public static MedicalCareCategory GetCare(this Pawn pawn)
		{
			if (TendAdvice != null)
			{
				var care = TendAdvice(pawn);
				Log.Message($"Pharmacist tend advicefor {pawn} is {care}");
				return care;
			}
			return pawn.playerSettings?.medCare ?? MedicalCareCategory.Best;
		}
	}
}

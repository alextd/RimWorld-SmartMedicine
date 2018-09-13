using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using UnityEngine;


namespace SmartMedicine.SurgeryUnlimited
{
	public class PawnColumnWorker_UnlimitedSurgery : PawnColumnWorker
	{ 
		public override int GetMinWidth(PawnTable table)
		{
			return Mathf.Max(base.GetMinWidth(table), 28);
		}

		public override int GetMaxWidth(PawnTable table)
		{
			return Mathf.Min(base.GetMaxWidth(table), this.GetMinWidth(table));
		}

		public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
		{
			SurgeryUnlimitedGameComponent comp = SurgeryUnlimitedGameComponent.Get();
			bool enabled = comp.surgeryUnlimited.Contains(pawn);
			Widgets.Checkbox(rect.position, ref enabled, rect.width);
			comp.Set(pawn, enabled);
		}

		public override int Compare(Pawn a, Pawn b)
		{
			int ret = 0;
			SurgeryUnlimitedGameComponent comp = SurgeryUnlimitedGameComponent.Get();
			if (comp.surgeryUnlimited.Contains(a)) ret++;
			if (comp.surgeryUnlimited.Contains(b)) ret--;
			return ret;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;

namespace SmartMedicine
{
	public static class ReservationUtility
	{
		public static int ReserveAsMuchAsPossible(this Pawn p, LocalTargetInfo target, Job job, int maxPawns = 1, int stackCount = -1, ReservationLayerDef layer = null)
		{
			if (!p.Spawned) return 0;

			if (!target.HasThing) return p.Reserve(target, job, maxPawns, stackCount, layer) ? 1 : 0;

			Thing thing = target.Thing;
			int desired = stackCount == ReservationManager.StackCount_All ? thing.stackCount : stackCount;
			for (int tryCount = desired; tryCount > 0; tryCount --)
			{
				if (p.CanReserve(target, maxPawns, tryCount, layer) 
					&& p.Reserve(target, job, maxPawns, tryCount, layer))
					return tryCount;
			}

			return 0;
		}
	}
}

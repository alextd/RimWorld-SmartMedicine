using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;

namespace TD.Utilities
{
	public static class ReservationUtility
	{
		public static int ReserveAsMuchAsPossible(this Pawn p, LocalTargetInfo target, Job job, int maxPawns = 1, int stackCount = -1, ReservationLayerDef layer = null)
		{
			if (!p.Spawned) return 0;

			if (!target.HasThing) return p.Reserve(target, job, maxPawns, stackCount, layer) ? 1 : 0;

			int canDo = p.Map.reservationManager.CanReserveStack(p, target.Thing, maxPawns, layer);

			Thing thing = target.Thing;
			int desired = stackCount == ReservationManager.StackCount_All ? thing.stackCount : stackCount;
			desired = Math.Min(desired, thing.stackCount);
			canDo = Math.Min(canDo, desired);

			p.Reserve(target, job, maxPawns, canDo, layer);

			return canDo;
		}
	}
}

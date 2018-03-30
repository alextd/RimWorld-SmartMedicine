using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace SmartMedicine
{
	//Just custom job need to do this for the job.count
	public class JobDriver_StockUpOnMedicine : JobDriver_TakeInventory
	{
		public override bool TryMakePreToilReservations()
		{
			return this.pawn.Reserve(job.targetA, job, FindBestMedicine.maxPawns, job.count);
		}
	}

	public class JobDriver_StockDownOnMedicine : JobDriver_HaulToCell
	{
		public override bool TryMakePreToilReservations()
		{
			return this.pawn.Reserve(job.targetB, job);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			Toil carryToil = new Toil()
			{
				initAction = delegate
				{
					Pawn actor = this.pawn;
					Job curJob = this.job;
					Thing thing = curJob.GetTarget(TargetIndex.A).Thing;
					int dropCount = curJob.count;
					int carriedCount = actor.carryTracker.CarriedThing?.stackCount ?? 0;
					if (dropCount == 0 && carriedCount > 0)
						return;

					int canCarryCount = actor.carryTracker.AvailableStackSpace(thing.def);
					int startCarryCount = Mathf.Min(dropCount - carriedCount, canCarryCount);
					if (startCarryCount == 0)
					{
						actor.jobs.EndCurrentJob(JobCondition.Incompletable);
						return;
					}
					int carried = actor.carryTracker.TryStartCarry(thing, startCarryCount);
					if (carried == 0)
					{
						actor.jobs.EndCurrentJob(JobCondition.Incompletable);
					}
					job.count -= carried;
				}
			};

			yield return carryToil;
			yield return Toils_Haul.CarryHauledThingToCell(TargetIndex.B);
			yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.B, carryToil, true);
		}
	}
}
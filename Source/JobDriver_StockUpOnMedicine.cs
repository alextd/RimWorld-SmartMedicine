using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace SmartMedicine
{
	public class JobDriver_StockUpOnMedicine : JobDriver
	{
		public override bool TryMakePreToilReservations()
		{
			return this.pawn.Reserve(job.targetA, job, FindBestMedicine.maxPawns, job.count);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(TargetIndex.A);
			yield return Toils_Haul.TakeToInventory(TargetIndex.A, job.count);
		}
	}

	public class JobDriver_StockDownOnMedicine : JobDriver
	{
		public override bool TryMakePreToilReservations()
		{
			return this.pawn.Reserve(job.targetA, job);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			Toil carryToil = new Toil()
			{
				initAction = delegate
				{
					Pawn actor = this.pawn;
					Job curJob = this.job;
					Thing thing = curJob.GetTarget(TargetIndex.B).Thing;
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
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnForbidden(TargetIndex.A);
			yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.A, carryToil, true);
		}
	}
}
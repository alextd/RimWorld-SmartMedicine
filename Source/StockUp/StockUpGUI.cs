﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using System.Text;
using UnityEngine;
using Harmony;
using Verse;
using Verse.AI;
using RimWorld;

namespace SmartMedicine
{
	//private void CleanupCurrentJob(JobCondition condition, bool releaseReservations, bool cancelBusyStancesSoft = true)
	[HarmonyPatch(typeof(Pawn_JobTracker), "CleanupCurrentJob")]
	public static class CleanupCurrentJob_Patch
	{
		public static void Prefix(Pawn_JobTracker __instance)
		{
			if (__instance.curJob?.def == JobDefOf.TendPatient)
			{
				FieldInfo pawnField = AccessTools.Field(typeof(Pawn_JobTracker), "pawn");
				Pawn pawn = (Pawn)pawnField.GetValue(__instance);
				if (!pawn.Destroyed && pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null)
				{
					if (StockUpUtility.StockingUpOn(pawn, pawn.carryTracker.CarriedThing))
						pawn.inventory.innerContainer.TryAddOrTransfer(pawn.carryTracker.CarriedThing);
				}
			}
		}
	}

	//ITab_Pawn_Gear
	//private void DrawThingRow(ref float y, float width, Thing thing, bool inventory = false)
	[HarmonyPatch(typeof(ITab_Pawn_Gear), "DrawThingRow")]
	public static class DrawThingRow_Patch
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il, MethodBase mb)
		{
			IList<LocalVariableInfo> locals = mb.GetMethodBody().LocalVariables;
			int textIndex = locals.First(l => l.LocalType == typeof(string)).LocalIndex;
			int labelRectIndex = 4;

			MethodInfo SelPawnForGearInfo = AccessTools.Property(typeof(ITab_Pawn_Gear), "SelPawnForGear").GetGetMethod(true);
			MethodInfo LabelInfo = AccessTools.Method(typeof(Widgets), nameof(Widgets.Label),
				new Type[] { typeof(Rect), typeof(string) });

			MethodInfo IsNutritionGivingIngestibleInfo = AccessTools.Property(typeof(ThingDef), nameof(ThingDef.IsNutritionGivingIngestible)).GetGetMethod();
			MethodInfo IsIngestibleInfo = AccessTools.Property(typeof(ThingDef), nameof(ThingDef.IsIngestible)).GetGetMethod();

			MethodInfo AddStockTextInfo = AccessTools.Method(typeof(DrawThingRow_Patch), nameof(DrawThingRow_Patch.AddStockText),
				new Type[] { typeof(Pawn), typeof(Thing), typeof(string) });
			MethodInfo AddIncDecButtonInfo = AccessTools.Method(typeof(DrawThingRow_Patch), nameof(DrawThingRow_Patch.AddIncDecButton),
				new Type[] { typeof(Pawn), typeof(Thing), typeof(Rect) });

			bool setStr = false;
			foreach (CodeInstruction i in instructions)
			{
				if(i.opcode == OpCodes.Callvirt && i.operand == IsNutritionGivingIngestibleInfo)
					yield return new CodeInstruction(OpCodes.Callvirt, IsIngestibleInfo);
				else yield return i;

				//stloc.s str1
				if (!setStr && i.opcode == OpCodes.Stloc_S && i.operand is LocalBuilder lb && lb.LocalIndex == textIndex)
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);//this
					yield return new CodeInstruction(OpCodes.Call, SelPawnForGearInfo);//this.SelPawnForGearInfo
					yield return new CodeInstruction(OpCodes.Ldarg_3);//thing
					yield return new CodeInstruction(OpCodes.Ldloc_S, textIndex);//text
					yield return new CodeInstruction(OpCodes.Call, AddStockTextInfo);//AddStockText(pawn, thing, text)
					yield return new CodeInstruction(OpCodes.Stloc_S, textIndex);//text = AddStockText(pawn, thing, text);

					setStr = true;
				}
				else if (i.opcode == OpCodes.Call && i.operand == LabelInfo)
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);//this
					yield return new CodeInstruction(OpCodes.Call, SelPawnForGearInfo);//this.SelPawnForGearInfo
					yield return new CodeInstruction(OpCodes.Ldarg_3);//thing
					yield return new CodeInstruction(OpCodes.Ldloc_S, labelRectIndex);// labelRect
					yield return new CodeInstruction(OpCodes.Call, AddIncDecButtonInfo);//AddIncDecButton(pawn, thing, rect)
				}
			}
		}

		public static string AddStockText(Pawn pawn, Thing thing, string text) =>
			AddStockText(pawn, thing.def, text);
		public static string AddStockText(Pawn pawn, ThingDef thingDef, string text)
		{
			if (!pawn.StockingUpOn(thingDef)) return text;

			return text + String.Format(" / {0}", pawn.StockUpCount(thingDef));
		}

		public static void AddIncDecButton(Pawn pawn, Thing thing, Rect rect) =>
			AddIncDecButton(pawn, thing.def, rect);
		public static void AddIncDecButton(Pawn pawn, ThingDef thingDef, Rect rect)
		{
			if (!pawn.StockingUpOn(thingDef)) return;

			Rect iconRect = rect.RightPartPixels(rect.height);
			int count = pawn.StockUpCount(thingDef);
			if (count > 0 && Widgets.ButtonImage(iconRect, TexButton.ReorderDown))
				pawn.SetStockCount(thingDef, count - 1);

			iconRect.x -= iconRect.width;
			if (Widgets.ButtonImage(iconRect, TexButton.ReorderUp))
				pawn.SetStockCount(thingDef, count + 1);
		}
	}
	[StaticConstructorOnStartup]
	public static class TexButton
	{
		public static readonly Texture2D ReorderUp = ContentFinder<Texture2D>.Get("UI/Buttons/ReorderUp", true);
		public static readonly Texture2D ReorderDown = ContentFinder<Texture2D>.Get("UI/Buttons/ReorderDown", true);
	}


	//ITab_Pawn_Gear
	//private void DrawThingRow(ref float y, float width, Thing thing, bool inventory = false)
	[HarmonyPatch(typeof(ITab_Pawn_Gear), "FillTab")]
	public static class FillTab_Patch
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il, MethodBase mb)
		{
			IList<LocalVariableInfo> locals = mb.GetMethodBody().LocalVariables;
			int yIndex = locals.First(l => l.LocalType == typeof(float)).LocalIndex;
			int viewRectIndex = 4;  //Is there a way to find this?.

			MethodInfo EventCurrentInfo = AccessTools.Property(typeof(Event), "current").GetGetMethod();

			MethodInfo SelPawnForGearInfo = AccessTools.Property(typeof(ITab_Pawn_Gear), "SelPawnForGear").GetGetMethod(true);

			MethodInfo RectWidthInfo = AccessTools.Property(typeof(Rect), "width").GetGetMethod();

			MethodInfo DrawMissingThingsInfo = AccessTools.Method(typeof(FillTab_Patch), nameof(FillTab_Patch.DrawMissingThings));

			foreach (CodeInstruction i in instructions)
			{
				if (i.opcode == OpCodes.Call && i.operand == EventCurrentInfo)
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);//this
					yield return new CodeInstruction(OpCodes.Call, SelPawnForGearInfo);//this.SelPawnForGearInfo
					yield return new CodeInstruction(OpCodes.Ldloca_S, yIndex);//ref y
					yield return new CodeInstruction(OpCodes.Ldloca_S, viewRectIndex);// viewrect
					yield return new CodeInstruction(OpCodes.Call, RectWidthInfo);//viewRect.width
					yield return new CodeInstruction(OpCodes.Call, DrawMissingThingsInfo);//DrawMissingThings(this.pawn, ref y, viewRect.width)
				}

				yield return i;
			}
		}

		public static void DrawMissingThings(Pawn pawn, ref float y, float width)
		{
			if (!Settings.Get().stockUp) return;

			foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
			{
				if (pawn.StockUpMissing(def))
					DrawMissingThingRow(pawn, ref y, width, def);
			}
			DrawStockUpButton(pawn, ref y, width);
		}

		public static void DrawStockUpButton(Pawn pawn, ref float y, float width)
		{
			if (!Settings.Get().stockUp) return;

			GUI.color = ThingLabelColor;

			Rect rect = new Rect(width / 3, y, width / 3, 28f);

			if (Widgets.ButtonText(rect, "StockUpSettings".Translate()))
				Find.WindowStack.Add(new Dialog_StockUp(pawn));

			y += 28f;
		}

		//From ITab_Pawn_Gear:
		private static readonly Color ThingLabelColor = new Color(0.9f, 0.9f, 0.9f, 1f);
		private static readonly Color HighlightColor = new Color(0.5f, 0.5f, 0.5f, 1f);

		public static void DrawMissingThingRow(Pawn pawn, ref float y, float width, ThingDef thingDef)
		{
			Rect rect = new Rect(0f, y, width, 28f);

			GUI.color = ThingLabelColor;
			Widgets.InfoCardButton(rect.width - 24f, y, thingDef);
			rect.width -= 24f;

			//private static readonly Color ThingLabelColor = new Color(0.9f, 0.9f, 0.9f, 1f)

			if (Mouse.IsOver(rect))
			{
				GUI.color = HighlightColor;
				GUI.DrawTexture(rect, TexUI.HighlightTex);
			}
			Widgets.ThingIcon(new Rect(4f, y, 28f, 28f), thingDef);

			Text.Anchor = TextAnchor.MiddleLeft;
			GUI.color = Color.red;
			Rect textRect = new Rect(36f, y, rect.width - 36f, rect.height);
			string text = DrawThingRow_Patch.AddStockText(pawn, thingDef, thingDef.LabelCap);
			Text.WordWrap = false;
			Widgets.Label(textRect, text.Truncate(textRect.width, null));
			Text.WordWrap = true;

			DrawThingRow_Patch.AddIncDecButton(pawn, thingDef, textRect);

			y += 28f;
		}
	}

	[HarmonyPatch(typeof(ITab_Pawn_Gear), "InterfaceDrop")]
	//private void InterfaceDrop(Thing t)
	public static class InterfaceDrop_Patch
	{
		public static void Postfix(Thing t, ITab_Pawn_Gear __instance)
		{
			if (!Settings.Get().stockUp) return;

			PropertyInfo SelPawnForGearInfo = AccessTools.Property(typeof(ITab_Pawn_Gear), "SelPawnForGear");
			Pawn pawn = (Pawn)SelPawnForGearInfo.GetValue(__instance, new object[] { });

			pawn.StockUpStop(t);
		}
	}

	public class Dialog_StockUp : Window
	{
		private Pawn pawn;
		private string title;
		private Vector2 scrollPosition = Vector2.zero;
		private float scrollViewHeight;
		private bool anything = false;

		public override Vector2 InitialSize
		{
			get
			{
				return new Vector2(500f, 300f);
			}
		}

		public Dialog_StockUp(Pawn p)
		{
			pawn = p;
			title = String.Format("StockUpSettingsForPawn".Translate(), p.NameStringShort);
			//absorbInputAroundWindow = true;
			closeOnEscapeKey = true;
			doCloseX = true;
			draggable = true;
			preventCameraMotion = false;
		}

		public override void DoWindowContents(Rect inRect)
		{
			Text.Font = GameFont.Medium;
			Widgets.Label(inRect.TopPartPixels(Text.LineHeight * 2), title);
			Text.Font = GameFont.Small;
			if (Prefs.DevMode && Widgets.ButtonText(inRect.TopPartPixels(Text.LineHeight * 2).RightPart(0.3f), "Stock ANYTHING"))
				anything = !anything;


			Rect botRect = inRect.BottomPartPixels(inRect.height - Text.LineHeight * 2);
			GUI.BeginGroup(botRect);
			Text.Font = GameFont.Small;
			GUI.color = Color.white;
			Rect outRect = new Rect(0f, 0f, botRect.width, botRect.height);
			Rect viewRect = new Rect(0f, 0f, botRect.width - 16f, this.scrollViewHeight);
			Widgets.BeginScrollView(outRect, ref this.scrollPosition, viewRect);

			float iconSize = Text.LineHeight * 2;

			float x = viewRect.xMin;
			float y = viewRect.yMin;
			Rect rowRect = new Rect(0, 0, iconSize * 2, iconSize);

			List<ThingDef> stockable = DefDatabase<ThingDef>.AllDefsListForReading.FindAll(
				t => t.EverHaulable &&
				anything ||
				(t.IsDrug || t.IsMedicine));
			foreach (ThingDef td in stockable)
			{
				rowRect.x = x;
				rowRect.y = y;
				Rect iconRect = rowRect.LeftHalf();
				Rect checkRect = iconRect.TopHalf().RightHalf();
				Rect countRect = rowRect.RightHalf().TopPart(3f / 4f).BottomPart(2f / 3f);

				x += rowRect.width;
				if (x + rowRect.width > viewRect.width)
				{
					x = viewRect.xMin;
					y += rowRect.height;
				}

				if (td.graphicData != null)
					Widgets.ThingIcon(iconRect, td);
				else
					Widgets.Label(iconRect, td.defName);
				TooltipHandler.TipRegion(iconRect, td.LabelCap);

				bool doIt = pawn.StockingUpOn(td);
				Widgets.DrawTextureFitted(checkRect, doIt ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex, 1);
				if (Widgets.ButtonInvisible(iconRect))
				{
					if (!doIt)
					{
						pawn.SetStockCount(td, 1);
					}
					else
					{
						pawn.StockUpStop(td);
						doIt = false;
					}
				}

				if (doIt)
				{
					int count = pawn.StockUpCount(td);
					string dummyStr = "" + count;
					Widgets.TextFieldNumeric<int>(countRect, ref count, ref dummyStr, 0, 9999);
					pawn.SetStockCount(td, count);
				}
			}
			if (Event.current.type == EventType.Layout)
			{
				scrollViewHeight = y + rowRect.height;
			}
			Widgets.EndScrollView();
			GUI.EndGroup();
			GUI.color = Color.white;
			Text.Anchor = TextAnchor.UpperLeft;
		}
		public override void WindowUpdate()
		{
			base.WindowUpdate();
			if (Find.Selector.SingleSelectedThing != pawn)
				Close();
		}
	}
}

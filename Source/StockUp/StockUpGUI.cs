using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using System.Text;
using UnityEngine;
using HarmonyLib;
using Verse;
using Verse.AI;
using RimWorld;

namespace SmartMedicine
{
	[StaticConstructorOnStartup]
	public static class PatchRPG
	{ 
		static PatchRPG()
		{
			Harmony harmony = new Harmony("uuugggg.rimworld.SmartMedicine.RPGInventory");

			Log.Message("Looking for RPG Inventory");
			//One could list all subtypes but what are the chances that'll be needed
			if (AccessTools.TypeByName("Sandy_Detailed_RPG_GearTab") is Type subtype)
			{
				Log.Message("Patching RPG Inventory");
				try
				{
					harmony.Patch(AccessTools.Method(subtype, "DrawThingRow"),
						transpiler: new HarmonyMethod(typeof(DrawThingRow_Patch), "Transpiler"));
					harmony.Patch(AccessTools.Method(subtype, "InterfaceDrop"),
						postfix: new HarmonyMethod(typeof(InterfaceDrop_Patch), "Postfix"));
					harmony.Patch(AccessTools.Method(subtype, "FillTab"),
						transpiler: new HarmonyMethod(typeof(FillTab_Patch), "Transpiler"));
				}
				catch(Exception )
				{
					Verse.Log.Warning("Smart medicine tried to patch RPG Inventory but I guess it failed? Geez. I guess that means no Stock up Button");
					harmony.UnpatchAll(harmony.Id);
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
			MethodInfo LabelCapInfo = AccessTools.Property(typeof(Thing), nameof(Thing.LabelCap)).GetGetMethod().GetBaseDefinition();
			MethodInfo DescriptionDetailedInfo = AccessTools.Property(typeof(Thing), nameof(Thing.DescriptionDetailed)).GetGetMethod();

			MethodInfo SelPawnForGearInfo = AccessTools.Property(typeof(ITab_Pawn_Gear), "SelPawnForGear").GetGetMethod(true);
			MethodInfo LabelInfo = AccessTools.Method(typeof(Widgets), nameof(Widgets.Label),
				new Type[] { typeof(Rect), typeof(string) });

			MethodInfo IsNutritionGivingIngestibleInfo = AccessTools.Property(typeof(ThingDef), nameof(ThingDef.IsNutritionGivingIngestible)).GetGetMethod();
			MethodInfo IsIngestibleInfo = AccessTools.Property(typeof(ThingDef), nameof(ThingDef.IsIngestible)).GetGetMethod();

			MethodInfo AddStockTextInfo = AccessTools.Method(typeof(DrawThingRow_Patch), nameof(AddStockText));
			MethodInfo AddStockTipInfo = AccessTools.Method(typeof(DrawThingRow_Patch), nameof(AddStockTip));
			MethodInfo LabelIncDecButtonInfo = AccessTools.Method(typeof(DrawThingRow_Patch), nameof(LabelIncDecButton));
			FieldInfo defInfo = AccessTools.Field(typeof(Thing), nameof(Thing.def));

			foreach (CodeInstruction i in instructions)
			{
				if (i.Calls(IsNutritionGivingIngestibleInfo))
					yield return new CodeInstruction(OpCodes.Callvirt, IsIngestibleInfo);
				else if (i.Calls(LabelInfo))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);//this
					yield return new CodeInstruction(OpCodes.Call, SelPawnForGearInfo);//this.SelPawnForGearInfo
					yield return new CodeInstruction(OpCodes.Ldarg_3);//thing
					yield return new CodeInstruction(OpCodes.Ldfld, defInfo);//thing.def
					yield return new CodeInstruction(OpCodes.Call, LabelIncDecButtonInfo);//LabelIncDecButton(Rect, string, pawn, thing.def)
				}
				else yield return i;

				if (i.Calls(LabelCapInfo))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);//this
					yield return new CodeInstruction(OpCodes.Call, SelPawnForGearInfo);//this.SelPawnForGearInfo
					yield return new CodeInstruction(OpCodes.Ldarg_3);//thing
					yield return new CodeInstruction(OpCodes.Ldfld, defInfo);//thing.def
					yield return new CodeInstruction(OpCodes.Call, AddStockTextInfo);//AddStockText(text, pawn, thing.def)
				}
				else if (i.Calls(DescriptionDetailedInfo))
				{
						yield return new CodeInstruction(OpCodes.Ldarg_0);//this
						yield return new CodeInstruction(OpCodes.Call, SelPawnForGearInfo);//this.SelPawnForGearInfo
						yield return new CodeInstruction(OpCodes.Ldarg_3);//thing
						yield return new CodeInstruction(OpCodes.Ldfld, defInfo);//thing.def
						yield return new CodeInstruction(OpCodes.Call, AddStockTipInfo);//AddStockTip(text, pawn, thing.def)
				}
			}
		}

		public static string AddStockText(string text, Pawn pawn, ThingDef thingDef)
		{
			if (!pawn.IsFreeColonist || pawn.Dead) return text;

			if (!pawn.StockingUpOn(thingDef)) return text;

			string addedText = String.Format(" / {0}", pawn.StockUpCount(thingDef));

			if (pawn.StockUpWants(thingDef) > 0 && !StockUpUtility.EnoughAvailable(thingDef, pawn.Map))
				addedText += " (" + "TD.Paused".Translate() + ")";

			return text + addedText;
		}

		public static string AddStockTip(string text, Pawn pawn, ThingDef thingDef)
		{
			if (!pawn.IsFreeColonist || pawn.Dead) return text;

			string addedText = "";
			if (pawn.StockUpWants(thingDef) > 0 && !StockUpUtility.EnoughAvailable(thingDef, pawn.Map))
				addedText = "TD.NotEnoughStockUp".Translate();

				if (text != "" && addedText != "")
				return addedText + "\n\n" + text;

			return addedText + text;
		}

		public static void LabelIncDecButton(Rect rect, string text, Pawn pawn, ThingDef thingDef)
		{
			Widgets.Label(rect, text);

			if (!pawn.IsFreeColonist || pawn.Dead) return;

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
		public static readonly Texture2D Copy = ContentFinder<Texture2D>.Get("UI/Buttons/Copy");
		public static readonly Texture2D Paste = ContentFinder<Texture2D>.Get("UI/Buttons/Paste");
		public static readonly Texture2D Ignore = Resources.Load<Texture2D>("Textures/UI/Icons/HostilityResponse/Ignore");
		public static readonly Texture2D Abandon = ContentFinder<Texture2D>.Get("UI/Buttons/Abandon");
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

			MethodInfo EventCurrentInfo = AccessTools.Property(typeof(Event), "current").GetGetMethod();

			MethodInfo SelPawnForGearInfo = AccessTools.Property(typeof(ITab_Pawn_Gear), "SelPawnForGear").GetGetMethod(true);

			MethodInfo RectWidthInfo = AccessTools.Property(typeof(Rect), "width").GetGetMethod();

			MethodInfo DrawMissingThingsInfo = AccessTools.Method(typeof(FillTab_Patch), nameof(FillTab_Patch.DrawMissingThings));

			//figure out what viewRect is via BeginScrollView
			CodeInstruction viewRectInst = null;
			MethodInfo BeginScrollViewInfo = AccessTools.Method(typeof(Widgets), nameof(Widgets.BeginScrollView));

			List<CodeInstruction> instList = instructions.ToList();
			for(int i=0;i<instList.Count;i++)
			{
				CodeInstruction inst = instList[i];

				if (inst.Calls(BeginScrollViewInfo))
				{
					viewRectInst = instList[i-2];
				}

				if (inst.Calls(EventCurrentInfo))
				{
					if (viewRectInst == null)
						Verse.Log.Error("Smart Medicine Couldn't find viewRectInst, that'll mean no stock up GUI");
					else
					{
						yield return new CodeInstruction(OpCodes.Ldarg_0);//this
						yield return new CodeInstruction(OpCodes.Call, SelPawnForGearInfo);//this.SelPawnForGearInfo
						yield return new CodeInstruction(OpCodes.Ldloca_S, yIndex);//ref y
						yield return viewRectInst;// viewrect
						//yield return new CodeInstruction(OpCodes.Call, RectWidthInfo);//viewRect.width //this is not working somehow
						yield return new CodeInstruction(OpCodes.Call, DrawMissingThingsInfo);//DrawMissingThings(this.pawn, ref y, viewRect)
					}
				}

				yield return inst;
			}
		}

		public static void DrawMissingThings(Pawn pawn, ref float y, Rect viewRect)
		{
			if (!pawn.IsFreeColonist || pawn.Dead) return;

			if (!Settings.Get().stockUp) return;

			foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
			{
				if (pawn.StockUpMissing(def))
					DrawMissingThingRow(pawn, ref y, viewRect.width, def);
			}
			DrawStockUpButton(pawn, ref y, viewRect.width);
		}

		public static void DrawStockUpButton(Pawn pawn, ref float y, float width)
		{
			if (!Settings.Get().stockUp) return;

			GUI.color = ThingLabelColor;

			Rect rect = new Rect(width / 4, y, width / 2, 28f);

			if (Widgets.ButtonText(rect, "TD.StockUpSettings".Translate()))
				Find.WindowStack.Add(new Dialog_StockUp(pawn));


			Rect iconRect = rect.RightPartPixels(28f);
			if (pawn.StockingUpOnAnything())
			{
				iconRect.x += 28f;
				TooltipHandler.TipRegion(iconRect, "TD.ClearStockUp".Translate());
				if (Widgets.ButtonImage(iconRect, TexButton.Abandon))
					pawn.StockUpClearSettings();
			}

			iconRect.x = rect.x - 28f;
			if (StockUpUtility.CopiedPawn() == pawn)
			{
				TooltipHandler.TipRegion(iconRect, String.Format("TD.CancelCopyStockUp".Translate(), pawn.Name.ToStringShort));
				if (Widgets.ButtonImage(iconRect, TexButton.Ignore))
					StockUpUtility.StockUpCopySettings(null);
			}
			else
			{
				if (StockUpUtility.CopiedPawn() != null)
				{
					TooltipHandler.TipRegion(iconRect, String.Format("TD.CopyStockUpFrom".Translate(), StockUpUtility.CopiedPawn().Name.ToStringShort));
					if (Widgets.ButtonImage(iconRect, TexButton.Paste))
						pawn.StockUpPasteSettings();
				}
				iconRect.x -= 28f;
				if (pawn.StockingUpOnAnything() && Widgets.ButtonImage(iconRect, TexButton.Copy))
					pawn.StockUpCopySettings();
			}

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
			string text = DrawThingRow_Patch.AddStockText(thingDef.LabelCap, pawn, thingDef);
			Text.WordWrap = false;
			DrawThingRow_Patch.LabelIncDecButton(textRect, text.Truncate(textRect.width, null), pawn, thingDef);
			Text.WordWrap = true;

			string tip = DrawThingRow_Patch.AddStockTip("", pawn, thingDef);
			if (tip != "")
				TooltipHandler.TipRegion(textRect, tip);

			y += 28f;
		}
	}

	[HarmonyPatch(typeof(ITab_Pawn_Gear), "InterfaceDrop")]
	//private void InterfaceDrop(Thing t)
	public static class InterfaceDrop_Patch
	{
		public static PropertyInfo SelPawnForGearInfo = AccessTools.Property(typeof(ITab_Pawn_Gear), "SelPawnForGear");
		public static void Postfix(Thing t, ITab_Pawn_Gear __instance)
		{
			if (!Settings.Get().stockUp) return;

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
			title = String.Format("TD.StockUpSettingsForPawn".Translate(), p.Name.ToStringShort);
			//absorbInputAroundWindow = true;
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

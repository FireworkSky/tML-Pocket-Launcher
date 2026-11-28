using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace MobileJoystick;

// Screen-space artwork only. Layout, hit testing and gesture ownership belong
// to the existing input system. Default control artwork comes from texture packs.
internal static class NativeControlTheme
{
	private static readonly Dictionary<string, Texture2D> geometry = new();
	private static readonly Color Foreground = new(218, 232, 234);

	internal static void Unload()
	{
		foreach (Texture2D texture in geometry.Values) texture.Dispose();
		geometry.Clear();
	}

	internal static void Surface(SpriteBatch batch, Vector2 center, float radius, Color color, bool pressed = false)
	{
		if (radius <= 0f) return;
		DefaultControlTextures.TryDrawDefault(batch, pressed ? "PhonePressedCircle" : "PhonePurpleCircle",
			center, radius * 2f, color);
	}

	internal static void Stick(SpriteBatch batch, MobileJoystickRole role, Vector2 center,
		float radius, Vector2 offset, Color color, bool pressed = false, bool selected = false, string textureName = null, Item itemOverride = null)
	{
		bool defaultKnob = UsesDefaultKnob(role, textureName);
		bool showRightClickItem = defaultKnob && role == MobileJoystickRole.Aim && itemOverride == null
			&& MobileJoystickInput.AimRightClickMode;
		Item rightClickItem = showRightClickItem ? MobileJoystickInput.GetHeldItem() : null;
		bool hasRightClickItem = rightClickItem is { IsAir: false };
		DefaultControlTextures.TryDrawDefault(batch, "JoystickPad", center, radius * 2f, color);
		Vector2 knob = center + offset * radius * .46f;
		string knobTexture = !defaultKnob ? textureName : role switch {
			MobileJoystickRole.Move => "JoystickKnob",
			MobileJoystickRole.Grapple => "PhoneEmptyGrappleGlyph",
			MobileJoystickRole.Aim when !hasRightClickItem => MobileControlArtwork.AimKnobTexture,
			_ => "ItemButtonFrame"
		};
		bool customKnob = DefaultControlTextures.TryDraw(batch, knobTexture, knob, radius,
			pressed || selected ? Color.White : color);
		if (!customKnob && defaultKnob) DefaultControlTextures.TryDrawDefault(batch, knobTexture, knob, radius,
			pressed || selected ? Color.White : color);
		if (role == MobileJoystickRole.Aim) {
			if (hasRightClickItem) MobileJoystickSystem.DrawItemIcon(batch, rightClickItem, knob, radius * .65f, color);
			if (MobileJoystickInput.AimAndUseMode && defaultKnob)
				MobileJoystickSystem.DrawItemIcon(batch, itemOverride ?? MobileJoystickInput.GetHeldItem(), knob, radius * .65f, color);
		}
	}

	internal static void RalJoystick(SpriteBatch batch, Vector2 center, float radius, Vector2 offset, Color color,
		bool pressed, float knobRatio = .35f, Color? background = null, Color? stroke = null,
		float opacity = .4f, float borderOpacity = 1, float knobOpacity = .8f, bool drawBackground = true, bool drawKnob = true, float strokeWidth = 2,
		Item knobItem = null, float baseRatio = .75f)
	{
		if (radius <= 0) return;
		float alpha = color.A / 255f;
		if (drawBackground) {
			DrawMask(batch, "Disc", center, radius * baseRatio * 2f, RalControlStyle.Paint(background ?? new Color(68, 68, 68), opacity * .8f * alpha));
			if (strokeWidth > 0) Outline(batch, center, radius * baseRatio, strokeWidth, RalControlStyle.Paint(stroke ?? Color.White, borderOpacity * alpha));
		}
		if (drawKnob && knobRatio > 0) {
			Vector2 displacement = offset.LengthSquared() > .75f * .75f ? Vector2.Normalize(offset) * .75f : offset;
			Vector2 knob = center + displacement * radius;
			Color idle = RalControlStyle.Dark(background ?? new Color(68, 68, 68)) ? RalControlStyle.Knob : new Color(204, 204, 204);
			DrawMask(batch, "Disc", knob, radius * knobRatio * 2, RalControlStyle.Paint(pressed ? RalControlStyle.PressedKnob : idle, knobOpacity * alpha));
			Outline(batch, knob, radius * knobRatio * .8f, 1, Color.White * (knobOpacity * .4f * alpha));
			MobileJoystickSystem.DrawItemIcon(batch, knobItem, knob, radius * knobRatio * 1.4f, color);
		}
	}
	private static void Outline(SpriteBatch batch, Vector2 center, float radius, float width, Color color)
	{
		if (radius <= 0 || width <= 0) return;
		// Quantize normalized line width to bound the cache during editor resizing.
		int units = Math.Clamp((int)MathF.Round(width / (radius * 2) * 1024), 1, 512);
		string key = "outline-" + units;
		if (!geometry.TryGetValue(key, out Texture2D texture)) {
			var canvas = new GlyphCanvas(128);
			float thickness = units / 1024f;
			canvas.Ring(.5f, .5f, .5f - thickness / 2, thickness);
			texture = new Texture2D(Main.instance.GraphicsDevice, 128, 128);
			texture.SetData(canvas.Pixels);
			geometry.Add(key, texture);
		}
		batch.Draw(texture, center, null, color, 0, texture.Size() / 2, radius * 2 / texture.Width, SpriteEffects.None, 0);
	}

	internal static bool UsesDefaultKnob(MobileJoystickRole role, string textureName)
		=> MobileControlArtwork.UsesDefaultTexture(textureName, role == MobileJoystickRole.Move ? "JoystickKnob" : "AttackAimUse")
			|| role == MobileJoystickRole.Aim && MobileControlArtwork.UsesDefaultTexture(textureName, "AttackAimOnly");

	internal static void Glyph(SpriteBatch batch, string name, Vector2 center, float size, Color color)
	{
		if (DefaultControlTextures.TryDrawDefault(batch, PackGlyphName(name), center, size, color)) return;
		float opacity = color.A / 255f;
		DrawMask(batch, name, center + Vector2.One * Math.Max(1f, size / 35f), size, Color.Black * (.5f * opacity));
		DrawMask(batch, name, center, size, color.MultiplyRGB(Foreground));
	}

	internal static void Reticle(SpriteBatch batch, Vector2 center, float size, Color color)
		=> DefaultControlTextures.TryDrawDefault(batch, "AimReticle", center, size, color);

	internal static string RadialCenterTexture => "PhoneTealCircle";
	internal static string RadialBaseTexture => "PhonePurpleCircle";
	internal static string RadialRingTexture => "PhoneRadialRing";

	internal static void Radial(SpriteBatch batch, Vector2 center, float radius, Color color)
	{
		float diameter = radius * 5.6f;
		DefaultControlTextures.TryDrawDefault(batch, RadialBaseTexture, center, diameter, color);
		Color ringColor = color.MultiplyRGB(new Color(142, 124, 220));
		DefaultControlTextures.TryDrawDefault(batch, RadialRingTexture, center, diameter, ringColor);
		DefaultControlTextures.TryDrawDefault(batch, RadialCenterTexture, center, radius * 2f, color);
	}

	internal static void DrawMask(SpriteBatch batch, string name, Vector2 center, float size, Color color)
	{
		Texture2D texture = Mask(name);
		batch.Draw(texture, center, null, color, 0f, texture.Size() * .5f,
			size / texture.Width, SpriteEffects.None, 0f);
	}

	// Compatibility for serialized artwork names and existing editor callbacks.
	// Resolve pack artwork first, then generated geometry or Terraria assets.
	internal static Texture2D Texture(string name)
	{
		int item = name switch {
			"Settings" or "PhoneMenuGlyph" => ItemID.Wrench,
			"GrappleHook" or "GrappleHookIdle" or "PhoneGrappleGlyph" => ItemID.GrapplingHook,
			"Heal" => ItemID.LesserHealingPotion,
			"Mana" => ItemID.LesserManaPotion,
			"Buff" or "RadialBuff" => MobileControlArtwork.PotionItemType(MobileControlAction.Buff),
			"Mount" or "PhoneMountGlyph" => ItemID.SlimySaddle,
			_ => 0
		};
		if (item > 0) { Main.instance.LoadItem(item); return TextureAssets.Item[item].Value; }
		if (DefaultControlTextures.GetTexture(name) is Texture2D packed) return packed;
		return Mask(Symbol(name));
	}

	internal static bool Legacy(SpriteBatch batch, string name, Vector2 center, float size, Color color)
	{
		if (name == "QuickPotions") {
			MobileControlArtwork.DrawQuickPotions(batch, center, size / 2f, color);
			return true;
		}
		if (name is "Heal" or "Mana" or "Buff" or "RadialBuff") {
			MobileControlArtwork.DrawPotionItemIcon(batch, name == "Heal" ? MobileControlAction.Heal
				: name == "Mana" ? MobileControlAction.Mana : MobileControlAction.Buff, center, size, color);
			return true;
		}
		return DefaultControlTextures.TryDrawDefault(batch, name, center, size, color);
	}

	private static string Symbol(string name) => name switch {
		"MoveJumpHint" or "MoveJumpHintOriginal" or "Jump" or "PhoneJumpGlyph" or "EditorJumpGlyph" => "Jump",
		"JoystickKnob" or "DirectionalArrowsOriginal" or "EditorMove" => "Move",
		"JoystickArrowsOriginal" or "AttackAimUse" or "AimUseMode" or "PhoneAimModeGlyph" => "AimUse",
		"AimReticle" or "JoystickFrame" or "PhoneRadialRing" => "Ring",
		"PhoneAimCross" or "EditorAimCross" or "ZoomInPlus" or "AimOnlyMode" => "Plus",
		"ZoomIn" or "PhoneZoomInGlyph" => "ZoomIn",
		"ZoomOut" or "PhoneZoomOutGlyph" => "ZoomOut",
		"ZoomPair" => "Zoom",
		"Chat" or "ChatBubble" => "Chat",
		"Map" => "Map",
		"MapStyle" => "Pin",
		"SmartCursor" or "SmartCursorOff" or "PhoneSmartCursorGlyph" or "PhoneSmartCursorOnGlyph" => "Smart",
		"TargetLockOn" or "TargetLockOff" or "PhoneTargetGlyph" => "Target",
		"PhoneInteractGlyph" or "PhoneSmartInteractGlyph" => "Hand",
		"PhoneSwapGlyph" => "Swap",
		"PhoneAutoSelectGlyph" or "PhoneUseGlyph" => "Use",
		"PhoneUseProfileGlyph" => "Profile",
		"PhoneLoadoutGlyph" => "Loadout",
		"PhoneRadialGlyph" => "Radial",
		"EditorSimple" => "Move",
		"EditorAdvanced" => "AimUse",
		"EditorCustom" => "Settings",
		"EditorJoystick" => "Ring",
		"EditorRadial" => "Radial",
		"EditorGear" or "Settings" or "MobileDelete" or "Trash" => "Settings",
		"PhoneUnboundGlyph" or "EditorUnbound" => "Unbound",
		"QuickPotions" => "Potions",
		"BackpackIcon" or "PhoneInventoryGlyph" => "Bag",
		_ => "Unbound"
	};

	private static string PackGlyphName(string name) => name switch {
		"Move" => "JoystickKnob",
		"AimUse" => "AttackAimUse",
		"Plus" => "PhoneAimCross",
		"Target" => "PhoneTargetGlyph",
		"Smart" => "PhoneSmartCursorGlyph",
		"Hand" => "PhoneInteractGlyph",
		"Swap" => "PhoneSwapGlyph",
		"Use" => "PhoneUseGlyph",
		"Profile" => "PhoneUseProfileGlyph",
		"Loadout" => "PhoneLoadoutGlyph",
		"Radial" => "PhoneRadialGlyph",
		"Unbound" => "PhoneUnboundGlyph",
		_ => name
	};

	private static Texture2D Mask(string name)
	{
		if (geometry.TryGetValue(name, out Texture2D texture)) return texture;
		var canvas = new GlyphCanvas(128);
		switch (name) {
			case "Disc": canvas.Circle(.5f, .5f, .5f); break;
			case "Help": canvas.Circle(.5f,.35f,.24f); canvas.Line(.39f,.55f,.43f,.73f,.08f); canvas.Line(.61f,.55f,.57f,.73f,.08f); canvas.Line(.43f,.78f,.57f,.78f,.07f); break;
			case "Border": canvas.Ring(.5f, .5f, .48f, .014f); break;
			case "Ring": canvas.Ring(.5f, .5f, .39f, .045f); break;
			case "Plus": canvas.Line(.5f,.18f,.5f,.82f,.08f); canvas.Line(.18f,.5f,.82f,.5f,.08f); break;
			case "Move":
				for (int i=0;i<4;i++) { float angle=i*MathHelper.PiOver2; Vector2 d=new(MathF.Sin(angle),-MathF.Cos(angle)); Vector2 p=new(-d.Y,d.X); Vector2 c=new Vector2(.5f)+d*.28f; canvas.Triangle(c+d*.1f,c-d*.06f+p*.09f,c-d*.06f-p*.09f); } break;
			case "Jump":
				foreach(float y in new[]{.35f,.66f}) { canvas.Line(.23f,y+.14f,.5f,y-.12f,.065f); canvas.Line(.5f,y-.12f,.77f,y+.14f,.065f); } break;
			case "AimUse":
				canvas.Line(.5f,.15f,.5f,.3f,.065f); canvas.Line(.5f,.7f,.5f,.85f,.065f); canvas.Line(.15f,.5f,.3f,.5f,.065f); canvas.Line(.7f,.5f,.85f,.5f,.065f); break;
			case "Target": canvas.Ring(.5f,.5f,.29f,.04f); canvas.Circle(.5f,.5f,.06f); goto case "AimUse";
			case "Smart": canvas.Box(.23f,.23f,.77f,.77f,.065f); break;
			case "ZoomIn": case "ZoomOut": case "Zoom":
				canvas.Ring(.42f,.4f,.23f,.055f); canvas.Line(.59f,.57f,.84f,.82f,.085f); canvas.Line(.29f,.4f,.55f,.4f,.045f);
				if(name!="ZoomOut") canvas.Line(.42f,.27f,.42f,.53f,.045f); break;
			case "Chat": canvas.Box(.15f,.2f,.85f,.67f,.055f); canvas.Line(.26f,.67f,.26f,.84f,.055f); canvas.Line(.26f,.84f,.46f,.67f,.055f); for(int i=0;i<3;i++) canvas.Circle(.32f+i*.18f,.43f,.035f); break;
			case "Map": canvas.Line(.17f,.22f,.17f,.79f,.05f); canvas.Line(.17f,.22f,.38f,.3f,.05f); canvas.Line(.38f,.3f,.63f,.2f,.05f); canvas.Line(.63f,.2f,.83f,.28f,.05f); canvas.Line(.83f,.28f,.83f,.85f,.05f); canvas.Line(.17f,.79f,.38f,.87f,.05f); canvas.Line(.38f,.87f,.63f,.77f,.05f); canvas.Line(.63f,.77f,.83f,.85f,.05f); canvas.Line(.38f,.3f,.38f,.87f,.04f); canvas.Line(.63f,.2f,.63f,.77f,.04f); break;
			case "Pin": canvas.Ring(.5f,.36f,.2f,.05f); canvas.Line(.32f,.46f,.5f,.84f,.055f); canvas.Line(.68f,.46f,.5f,.84f,.055f); break;
			case "Hand": canvas.Line(.46f,.67f,.46f,.2f,.09f); canvas.Line(.57f,.65f,.57f,.3f,.09f); canvas.Line(.68f,.64f,.68f,.4f,.09f); canvas.Line(.36f,.65f,.27f,.48f,.09f); canvas.Line(.34f,.63f,.46f,.84f,.12f); canvas.Line(.46f,.8f,.66f,.72f,.15f); break;
			case "Swap": canvas.Line(.2f,.32f,.8f,.32f,.06f); canvas.Line(.8f,.32f,.64f,.18f,.06f); canvas.Line(.2f,.68f,.8f,.68f,.06f); canvas.Line(.2f,.68f,.36f,.82f,.06f); break;
			case "Use": canvas.Line(.22f,.79f,.77f,.24f,.12f); canvas.Line(.17f,.59f,.41f,.83f,.05f); break;
			case "Loadout": canvas.Box(.2f,.27f,.8f,.8f,.045f); canvas.Box(.35f,.12f,.65f,.27f,.04f); canvas.Line(.2f,.48f,.8f,.48f,.05f); break;
			case "Bag": canvas.Box(.23f,.32f,.77f,.83f,.055f); canvas.Box(.35f,.15f,.65f,.32f,.05f); canvas.Line(.23f,.48f,.77f,.48f,.055f); canvas.Box(.43f,.45f,.57f,.61f,.04f); break;
			case "Profile": canvas.Line(.3f,.32f,.7f,.32f,.1f); canvas.Line(.25f,.35f,.17f,.7f,.1f); canvas.Line(.75f,.35f,.83f,.7f,.1f); canvas.Line(.19f,.7f,.4f,.57f,.08f); canvas.Line(.81f,.7f,.6f,.57f,.08f); break;
			case "Potions": canvas.Box(.25f,.35f,.45f,.8f,.06f); canvas.Line(.31f,.2f,.39f,.2f,.08f); canvas.Line(.35f,.2f,.35f,.35f,.08f); canvas.Box(.56f,.44f,.76f,.8f,.06f); canvas.Line(.62f,.29f,.7f,.29f,.08f); canvas.Line(.66f,.29f,.66f,.44f,.08f); break;
			case "Radial": for(int i=0;i<4;i++) canvas.Circle(.5f+MathF.Sin(i*MathHelper.PiOver2)*.28f,.5f-MathF.Cos(i*MathHelper.PiOver2)*.28f,.085f); break;
			case "Settings":
				canvas.Ring(.5f,.5f,.19f,.08f);
				for (int i=0;i<8;i++) {
					float a=i*MathHelper.PiOver4;
					canvas.Line(.5f+MathF.Sin(a)*.23f,.5f-MathF.Cos(a)*.23f,
						.5f+MathF.Sin(a)*.39f,.5f-MathF.Cos(a)*.39f,.07f);
				}
				break;
			case "Trash":
				canvas.Box(.28f,.31f,.72f,.82f,.06f);
				canvas.Line(.22f,.23f,.78f,.23f,.07f);
				canvas.Line(.39f,.14f,.61f,.14f,.07f);
				break;
			default: for(int i=0;i<8;i++) { float a=i*MathHelper.PiOver4; canvas.Circle(.5f+MathF.Sin(a)*.29f,.5f+MathF.Cos(a)*.29f,.023f); } break;
		}
		texture = new Texture2D(Main.instance.GraphicsDevice, 128, 128);
		texture.SetData(canvas.Pixels);
		geometry.Add(name, texture);
		return texture;
	}

	// One-pixel analytical coverage produces premultiplied white masks. Build
	// once per symbol; drawing performs no readbacks, Begin/End or GPU allocation.
	private sealed class GlyphCanvas
	{
		internal readonly Color[] Pixels;
		private readonly int size;
		internal GlyphCanvas(int size) { this.size=size; Pixels=new Color[size*size]; }
		private void Shape(Func<Vector2,float> distance)
		{
			for(int y=0;y<size;y++) for(int x=0;x<size;x++) {
				float alpha=MathHelper.Clamp(.5f-distance(new Vector2((x+.5f)/size,(y+.5f)/size))*size,0f,1f);
				byte coverage=(byte)MathF.Round(alpha*255);
				if(coverage>Pixels[y*size+x].A) Pixels[y*size+x]=new Color(coverage,coverage,coverage,coverage);
			}
		}
		internal void Circle(float x,float y,float radius) => Shape(p=>Vector2.Distance(p,new Vector2(x,y))-radius);
		internal void Ring(float x,float y,float radius,float width) => Shape(p=>Math.Abs(Vector2.Distance(p,new Vector2(x,y))-radius)-width/2);
		internal void Line(float x,float y,float xx,float yy,float width)
		{
			Vector2 a=new(x,y),b=new(xx,yy),d=b-a;
			Shape(p=>Vector2.Distance(p,a+d*MathHelper.Clamp(Vector2.Dot(p-a,d)/d.LengthSquared(),0,1))-width/2);
		}
		internal void Box(float x,float y,float xx,float yy,float width) { Line(x,y,xx,y,width);Line(xx,y,xx,yy,width);Line(xx,yy,x,yy,width);Line(x,yy,x,y,width); }
		internal void Triangle(Vector2 a,Vector2 b,Vector2 c)
		{
			float Edge(Vector2 p,Vector2 u,Vector2 v) => ((p.X-u.X)*(v.Y-u.Y)-(p.Y-u.Y)*(v.X-u.X))/Vector2.Distance(u,v);
			float sign=Edge(c,a,b)<0?-1f:1f;
			Shape(p=>-Math.Min(sign*Edge(p,a,b),Math.Min(sign*Edge(p,b,c),sign*Edge(p,c,a))));
		}
	}
}

using System;
using Microsoft.Xna.Framework;

namespace MobileJoystick;

internal readonly struct MobileInventoryLayout
{
	public readonly float Scale;
	public readonly int Width;
	public readonly int Height;
	public readonly int SideInset;
	public readonly int TopInset;
	private readonly MobileInterfaceLayoutProfile profile;
	private readonly Rectangle viewport;
	public Rectangle SafeViewport => new(SideInset, TopInset, Math.Max(1, Width - 2 * SideInset), Math.Max(1, Height - 2 * TopInset));
	public MobileInventoryLayout(int width, int height, int edgePadding = -1)
		: this(width, height, edgePadding, null)
	{
	}

	public MobileInventoryLayout(int width, int height, int edgePadding, MobileInterfaceLayoutProfile explicitProfile)
	{
		profile = explicitProfile ?? MobileJoystickConfig.Instance?.InterfaceLayout;
		viewport = new Rectangle(0, 0, width, height);
		Width = width;
		Height = height;
		float screenScale = Math.Min(width / 1280f, height / 576f);
		SideInset = (int)MathF.Ceiling(Math.Max(18, edgePadding < 0 ? MobileJoystickConfig.Instance?.EdgePadding ?? 18 : edgePadding) * screenScale);
		TopInset = Math.Max(2, (int)(4 * screenScale));
		Scale = Math.Min((width - 2 * SideInset) / 1280f, (height - 2 * TopInset) / 576f);
	}
	public Rectangle At(float x, float y, float w = 48, float h = 48) => new(
		SideInset + (int)MathF.Round(x * Scale), TopInset + (int)MathF.Round(y * Scale),
		Math.Max(1, (int)MathF.Round(w * Scale)), Math.Max(1, (int)MathF.Round(h * Scale)));
	public Rectangle Right(float x, float y, float w = 48, float h = 48) => At((Width - 2 * SideInset) / Scale - x - w, y, w, h);
	public Rectangle LeftTab(int index) => Apply(MobileInterfaceRegion.InventoryActions, At(0, 50 * index, 50, 50));
	public Rectangle RightTab(int index) => Apply(MobileInterfaceRegion.InventoryActions, Right(0, 50 * index, 50, 50));
	public Rectangle RightTabDefault(int index) => Right(0, 50 * index, 50, 50);
	public Rectangle Armor(int slot, int kind) => Gear((slot < 3 || slot >= 8 ? 5 : 2) + 2 - kind,
		slot < 3 ? slot : slot >= 8 ? slot - 5 : slot - 3);
	public Rectangle MiscEquipment(int slot, bool dye = false) => Gear(dye ? 0 : 1, slot);
	public Rectangle ModEquipmentButton => Apply(MobileInterfaceRegion.Equipment, Right(380, 260, 44, 44));
	public Rectangle LoadoutButton => Apply(MobileInterfaceRegion.Equipment, Right(330, 260, 44, 44));
	public Rectangle Defense => Apply(MobileInterfaceRegion.Equipment, Right(280, 260, 44, 44));
	public Rectangle Inventory(int slot)
	{
		Rectangle bounds = MobileJoystickConfig.Instance?.HotbarOrientation == MobileHotbarOrientation.Horizontal
			? InventoryCell(slot % 10, slot / 10)
			: InventoryDefault(slot);
		return Apply(MobileInterfaceRegion.InventoryItems, bounds);
	}
	public Rectangle InventoryDefault(int slot) => InventoryCell(slot / 5, slot % 5);
	private Rectangle InventoryCell(int column, int row) => At(54 + column * 50, 4 + row * 50);
	public Rectangle CoinAmmo(int slot) => Apply(MobileInterfaceRegion.CoinAmmo, At(572 + (slot >= 54 ? 50 : 0), 60 + (slot - 50) % 4 * 50, 44, 44));
	public Rectangle Gear(int column, int row) => Apply(MobileInterfaceRegion.Equipment, Right(54 + (7 - column) * 50, 4 + row * 50));
	public Rectangle Recipe(int index) => Apply(MobileInterfaceRegion.Crafting, Right(54 + index / 5 * 50, 4 + index % 5 * 50));
	public Rectangle CraftFilter => Apply(MobileInterfaceRegion.Crafting, Right(380, 260, 44, 44));
	public Rectangle CraftSearch => Apply(MobileInterfaceRegion.Crafting, Right(330, 260, 44, 44));
	public Rectangle CraftAction => Apply(MobileInterfaceRegion.Crafting, Right(4, 260, 62, 62));
	public Rectangle CraftAt(float x, float y, float w = 48, float h = 48) => Apply(MobileInterfaceRegion.Crafting, Right(x, y, w, h));
	public Rectangle CraftCategory(int index) => Apply(MobileInterfaceRegion.Crafting, Right(404 - index % 3 * 50, 54 + index / 3 * 50, 46, 46));
	public Rectangle CraftCategories => Apply(MobileInterfaceRegion.Crafting, Right(300, 50, 154, 204));
	public Rectangle CraftFilterButton(bool expanded) => expanded ? CraftFilter : Apply(MobileInterfaceRegion.Crafting, Right(130, 260, 44, 44));
	public Rectangle CraftSearchButton(bool expanded) => expanded ? CraftSearch : Apply(MobileInterfaceRegion.Crafting, Right(80, 260, 44, 44));
	public Rectangle CraftCategoryButton(int index, bool expanded) => expanded ? CraftCategory(index)
		: Apply(MobileInterfaceRegion.Crafting, Right(154 - index % 3 * 50, 54 + index / 3 * 50, 46, 46));
	public Rectangle CraftCategoryPanel(bool expanded) => expanded ? CraftCategories : Apply(MobileInterfaceRegion.Crafting, Right(50, 50, 154, 204));
	public Rectangle RecipeDetails(Rectangle anchor, float desiredWidth, float desiredHeight)
		// Saved layouts can put either column anywhere. The default bag edge is
		// not an available-width boundary after the user has moved the slots.
		=> ItemDetails(anchor, desiredWidth, desiredHeight);
	public Rectangle Container(int slot) => Right(54 + (9 - slot % 10) * 50, 36 + slot / 10 * 50);
	public Rectangle ShopArea => Right(0, 0, 560, 355);
	public Rectangle ShopSlot(int slot) => Right(54 + slot % 8 * 50, 4 + slot / 8 * 50);
	public Rectangle ShopBuy => Right(4, 260, 62, 62);
	public Rectangle ShopSell => At(8, 260, 62, 62);
	public Rectangle ItemDetails(Rectangle anchor, float desiredWidth, float desiredHeight)
	{
		int padding = Math.Max(1, (int)(6 * Scale));
		int left = SideInset + padding, right = Width - SideInset - padding;
		int width = Math.Max(1, Math.Min((int)Math.Min(desiredWidth, 360 * Scale), right - left));
		int height = Math.Max(1, Math.Min((int)desiredHeight, Math.Min(Height - padding * 2, (int)(340 * Scale))));
		int x = anchor.Center.X < Width / 2 ? anchor.Right + padding : anchor.Left - padding - width;
		int y = Math.Clamp(anchor.Top, padding, Math.Max(padding, Height - padding - height));
		return new Rectangle(Math.Clamp(x, left, Math.Max(left, right - width)), y, width, height);
	}
	public Rectangle LeftAction(int index) => Apply(MobileInterfaceRegion.InventoryActions, At(26 + index * 52, 260, 44, 44));
	public Rectangle LeftActionDefault(int index) => At(26 + index * 52, 260, 44, 44);
	public Rectangle InfoToggle(int index, int count) {
		float step = Math.Min(30f, 400f / Math.Max(1, count));
		return Apply(MobileInterfaceRegion.InventoryActions, Right(54 + (count - 1 - index) * step, 314, step - 2, 28));
	}
	public Rectangle DifficultyButton => MobileCalamityUiSystem.DifficultyBounds;
	public Rectangle InventoryTools => Apply(MobileInterfaceRegion.InventoryActions, At(404, 204, 156, 52));
	public Rectangle InventoryTool(int index) => Apply(MobileInterfaceRegion.InventoryActions, At(408 + index * 50, 208, 44, 44));
	public Rectangle RightAction(int index) => Apply(MobileInterfaceRegion.InventoryActions, Right(4 + index * 50, 260, 44, 44));
	public Rectangle RightActionDefault(int index) => Right(4 + index * 50, 260, 44, 44);
	public Rectangle LeftArea => At(0, 0, 670, 355);
	public Rectangle RightArea(bool container) => Right(0, 0, container ? 560 : 460, 355);

	public Rectangle RegionBounds(MobileInterfaceRegion region) => region switch {
		MobileInterfaceRegion.InventoryItems => At(54, 4, 498, 248),
		MobileInterfaceRegion.CoinAmmo => At(572, 60, 94, 194),
		MobileInterfaceRegion.InventoryActions => new Rectangle(SideInset, TopInset, SafeViewport.Width,
			Math.Max(1, (int)MathF.Round(355 * Scale))),
		MobileInterfaceRegion.Equipment => Right(0, 0, 460, 355),
		MobileInterfaceRegion.Crafting => Right(0, 0, 460, 400),
		_ => new Rectangle(0, 0, Width, Height)
	};

	public Rectangle Apply(MobileInterfaceRegion region, Rectangle rectangle)
	{
		if (profile == null || !profile.Has(region)) return rectangle;
		return MobileInterfaceLayoutProfile.Transform(profile.Get(region), rectangle, RegionBounds(region), viewport, SafeViewport);
	}
}

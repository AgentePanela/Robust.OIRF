using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.Client.UserInterface;
public sealed class StyleSheetManager
{
    [Dependency] private readonly IUserInterfaceManager _userInterfaceManager = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;

    //public Stylesheet SheetOmicrom { get; private set; } = default!;

    public const string StyleClassLabelGame = "LabelGame";

    public void Initialize()
    {
        var oirfFontRes = _resourceCache.GetResource<FontResource>("/Fonts/VcrOsd-mono.ttf");
        var bigFont = new VectorFont(oirfFontRes, 32);

        //SheetOmicron = new StyleOmicron(_resourceCache).Stylesheet;
            
        //_userInterfaceManager.Stylesheet = SheetOmicron;
        _userInterfaceManager.Stylesheet = new Stylesheet(new[]
        {
            new StyleRule(
            new SelectorElement(typeof(Label), new [] {StyleClassLabelGame}, null, null), new []
            {
                new StyleProperty(Label.StylePropertyFont, bigFont)
            })
        });
    }
    }
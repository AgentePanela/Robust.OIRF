using System;
using Robust.Client.State;
using Robust.Client.UserInterface.Screens;
using Robust.Shared.ContentPack;

namespace Content.Client.UserInterface.MainMenu;

public sealed class MainMenuState : State
{
    protected override Type? LinkedScreenType { get; } = typeof(MainMenu);

    protected override void Startup()
    {
    }

    protected override void Shutdown()
    {
    }
}

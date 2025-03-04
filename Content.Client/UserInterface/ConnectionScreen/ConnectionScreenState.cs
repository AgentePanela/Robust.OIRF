using System;
using Robust.Client.State;
using Robust.Client.UserInterface.Screens;
using Robust.Shared.ContentPack;

namespace Content.Client.UserInterface.ConnectionScreen;

public sealed class ConnectionScreenState : State
{
    protected override Type? LinkedScreenType { get; } = typeof(ConnectionScreen);

    protected override void Startup()
    {
    }

    protected override void Shutdown()
    {
    }
}

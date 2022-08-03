using System;
using System.Linq;
using System.Threading;
using Content.Shared;
using Content.Shared.Paddle;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Utility;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server;

[UsedImplicitly]
public sealed class PongSystem : SharedPongSystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IConfigurationManager _cfgManager = default!;
    [Dependency] private readonly ActorSystem _actorSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
        
    private float _restartTimer;
    private int _winThreshold;
    private const float BallScoreOffset = 0.9f;
    private const float BallInitialSpeed = 4f;
    private const float PaddleOffset = 1f;
    private MapId _map = MapId.Nullspace;

    private float _endTimer;

    private MapCoordinates MapCenter => new(ArenaSize / 2f, _map);
    private MapCoordinates PaddleOneStarting => new(new Vector2(PaddleOffset, MapCenter.Y), _map);
    private MapCoordinates PaddleTwoStarting => new(new Vector2(ArenaBox.Size.X - PaddleOffset, MapCenter.Y), _map);
        
    private IPlayerSession? _playerOne;
    private IPlayerSession? _playerTwo;

    private EntityUid? _paddleOne;
    private EntityUid? _paddleTwo;
    private EntityUid? _ball;

    /// <summary>
    ///     Returns the number of players who are connected or in-game.
    /// </summary>
    private int ConnectedPlayers => Filter.Empty().AddWhere(player => player.Status is SessionStatus.Connected or SessionStatus.InGame).Recipients.Count();
        
    public float BallScoreSpeedMultiplier
    {
        get
        {
            var score = 0;
                
            foreach (var paddle in EntityManager.EntityQuery<PaddleComponent>())
            {
                score += paddle.Score;
            }

            score /= 2;

            return 1f + IoCManager.Resolve<IConfigurationManager>().GetCVar(ContentCVars.BallSpeedupScore) * score;
        }
    }
        
    public override void Initialize()
    {
        base.Initialize();
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
            
        _cfgManager.OnValueChanged(ContentCVars.PongRestartTimer, OnPongRestartTimerChanged, true);
        _cfgManager.OnValueChanged(ContentCVars.PongWinThreshold, OnPongWinThresholdChanged, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void StartGame()
    {
        DebugTools.Assert(State == PongGameState.Start);

        if (ConnectedPlayers < 2)
            return;
            
        Logger.Info("Starting pong game...");
            
        ChangeState(PongGameState.Game);

        var players = _playerManager.ServerSessions.ToList();

        _playerOne = _random.PickAndTake(players);
        _playerTwo = _random.PickAndTake(players);

        _map = _mapManager.CreateMap();
            
        _ball = MakeBall();
        Comp<PhysicsComponent>(_ball.Value).LinearVelocity = Vector2.One * BallInitialSpeed * BallScoreSpeedMultiplier; 

        _paddleOne = MakePaddle(_playerOne, true);
        _paddleTwo = MakePaddle(_playerTwo, false);
            
        foreach (var player in players)
        {
            if(player.Status == SessionStatus.InGame)
                MakeObserver(player);
        }
    }
        
    private void EndGame()
    {
        DebugTools.Assert(State == PongGameState.Game);

        Logger.Info("Ending pong game...");

        if(_ball is {} ball)
            Del(ball);
        _endTimer = _restartTimer;
            
        ChangeState(PongGameState.End);
    }

    private void RestartGame()
    {
        DebugTools.Assert(State == PongGameState.End);

        Logger.Info("Restarting pong game...");
            
        // Clear all entities.
        foreach (var entity in EntityManager.GetEntities())
        {
            Del(entity);
        }
            
        _map = MapId.Nullspace;

        ChangeState(PongGameState.Start);
    }
        
    private void ChangeState(PongGameState state)
    {
        var old = State;
        State = state;
            
        var ev = new PongGameStateChangedEvent() 
            {Old = old, New = State};
            
        RaiseLocalEvent(ev);
        RaiseNetworkEvent(ev);
            
        Logger.Info($"Pong Game State changed from {old} to {State}!");
    }

    private EntityUid MakeBall()
    {
        DebugTools.Assert(State == PongGameState.Game);

        return EntityManager.SpawnEntity("Ball", MapCenter);
    }
        
    private EntityUid MakePaddle(IPlayerSession session, bool first)
    {
        DebugTools.Assert(State == PongGameState.Game);
            
        var entity = EntityManager.SpawnEntity("Paddle", first ? PaddleOneStarting : PaddleTwoStarting);
        var paddle = Comp<PaddleComponent>(entity);

        paddle.Player = session.Name;
        paddle.First = first;
        paddle.Dirty();
            
        session.AttachToEntity(entity);

        return entity;
    }
        
    private void MakeObserver(IPlayerSession session)
    {
        DebugTools.Assert(State == PongGameState.Game);

        // Empty entity.
        var entity = EntityManager.SpawnEntity(null, MapCenter);
            
        session.AttachToEntity(entity);
    }

    private void EnsurePaddlesHavePlayers()
    {
        DebugTools.Assert(State == PongGameState.Game);

        // Can't do much if player count is below the required.
        if (ConnectedPlayers < 2)
            return;
            
        IPlayerSession PaddleReassign(EntityUid paddle, IPlayerSession? ignore)
        {
            var players = Filter.Empty().AddWhere(s => s != ignore && s.Status == SessionStatus.InGame, _playerManager).Recipients.ToList();

            // No more players to choose from.
            if (players.Count == 0)
                return null!;
                
            var player = (IPlayerSession)_random.PickAndTake(players);

            _actorSystem.Attach(paddle, player);

            var paddleComp = Comp<PaddleComponent>(paddle);
            paddleComp.Player = player.Name;
            paddleComp.Dirty();
                
            return player;
        }

        // Delay reattaching the paddles to the next tick.
        Timer.Spawn(0, () =>
        {
            if (_playerOne is not {Status: SessionStatus.InGame})
            {
                if(HasComp<ActorComponent>(_paddleOne!.Value))
                    _playerOne?.DetachFromEntity();
                _playerOne = PaddleReassign(_paddleOne!.Value, _playerTwo);
            }

            if (_playerTwo is not {Status: SessionStatus.InGame})
            {
                if(HasComp<ActorComponent>(_paddleTwo!.Value))
                    _playerTwo?.DetachFromEntity();
                _playerTwo = PaddleReassign(_paddleTwo.Value, _playerOne);
            }
        }, CancellationToken.None);
    }
        
    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Connected)
        {
            args.Session.JoinGame();
            RaiseNetworkEvent(new PongGameStateChangedEvent() {Old = PongGameState.Start, New = State}, args.Session.ConnectedClient);
                
            if(State == PongGameState.Game)
                MakeObserver(args.Session);
        }
    }
        
    private void OnPongWinThresholdChanged(int threshold)
    {
        _winThreshold = threshold;
    }

    private void OnPongRestartTimerChanged(float time)
    {
        _restartTimer = time;
    }

    public override void Update(float delta)
    {
        switch (State)
        {
            case PongGameState.Start:
                StartGame();
                break;
            case PongGameState.Game:
                // Check that we still have a minimum of two players connected...
                if (ConnectedPlayers < 2)
                {
                    EndGame();
                    break;
                }

                var ballXform = Transform(_ball!.Value);
                var ballX = ballXform.WorldPosition.X;

                void OnScore()
                {
                    _audioSystem.PlayGlobal("/Audio/score.wav", Filter.Broadcast(), AudioParams.Default);
                        
                    // Reset ball.
                    ballXform.Coordinates = EntityCoordinates.FromMap(_mapManager, MapCenter);
                    var ballPhysics = Comp<PhysicsComponent>(_ball.Value);
                    var speed = ballPhysics.LinearVelocity.Normalized * BallInitialSpeed * BallScoreSpeedMultiplier;
                    ballPhysics.LinearVelocity = Vector2.Zero;
                    Timer.Spawn(1000, () => ballPhysics.LinearVelocity = speed, CancellationToken.None);
                }
                    
                var paddleOne = Comp<PaddleComponent>(_paddleOne!.Value);
                var paddleTwo = Comp<PaddleComponent>(_paddleTwo!.Value);
                    
                if (ballX < Transform(_paddleOne.Value).WorldPosition.X - BallScoreOffset)
                {
                    paddleTwo.Score++;
                    paddleTwo.Dirty();
                    OnScore();
                }
                    
                if (ballX > Transform(_paddleTwo.Value).WorldPosition.X + BallScoreOffset)
                {
                    paddleOne.Score++;
                    paddleOne.Dirty();
                    OnScore();
                }

                if (paddleOne.Score >= _winThreshold || paddleTwo.Score >= _winThreshold)
                {
                    EndGame();
                    break;
                }
                    
                EnsurePaddlesHavePlayers();
                    
                break;
            case PongGameState.End:
                _endTimer -= delta;
                    
                if(_endTimer <= 0f)
                    RestartGame();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Stl.Fusion
{
    public interface ILiveState : IComputedState
    {
        public new interface IOptions : IComputedState.IOptions
        {
            Func<ILiveState, IUpdateDelayer> UpdateDelayerFactory { get; set; }
            bool DelayFirstUpdate { get; set; }
        }

        IUpdateDelayer UpdateDelayer { get; }
    }

    public interface ILiveState<T> : IComputedState<T>, ILiveState, IDisposable
    { }

    public interface ILiveState<T, TOwn> : ILiveState<T>
    {
        bool InvalidateOnOwnStateUpdate { get; set; }
        bool UpdateOnOwnStateUpdate { get; set; }
        IMutableState<TOwn> OwnState { get; }
    }

    public abstract class LiveState<T> : ComputedState<T>, ILiveState<T>
    {
        public new class Options : ComputedState<T>.Options, ILiveState.IOptions
        {
            public static readonly Func<ILiveState, IUpdateDelayer> DefaultUpdateDelayerFactory =
                liveState => {
                    var services = liveState.ServiceProvider;

                    var updateDelayer = services.GetService<IUpdateDelayer<T>>();
                    if (updateDelayer != null)
                        return updateDelayer;

                    var options = services.GetService<UpdateDelayer<T>.Options>();
                    if (options != null)
                        return new UpdateDelayer<T>(options);

                    return services.GetRequiredService<IUpdateDelayer>();
                };


            public Func<ILiveState, IUpdateDelayer> UpdateDelayerFactory { get; set; } = DefaultUpdateDelayerFactory;
            public bool DelayFirstUpdate { get; set; } = false;
        }

        private readonly CancellationTokenSource _stopCts;

        protected CancellationToken StopToken { get; }
        protected Func<ILiveState<T>, IUpdateDelayer> UpdateDelayerFactory { get; }
        protected ILogger Log { get; }

        public IUpdateDelayer UpdateDelayer { get; private set; } = null!;
        public bool DelayFirstUpdate { get; }

        protected LiveState(
            Options options, IServiceProvider serviceProvider,
            object? argument = null, bool initialize = true)
            : base(options, serviceProvider, argument, false)
        {
            _stopCts = new CancellationTokenSource();
            StopToken = _stopCts.Token;
            Log = ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(GetType()) ?? NullLogger.Instance;
            UpdateDelayerFactory = options.UpdateDelayerFactory;
            DelayFirstUpdate = options.DelayFirstUpdate;
            if (initialize) Initialize(options);
        }

        protected override void Initialize(State<T>.Options options)
        {
            UpdateDelayer = UpdateDelayerFactory.Invoke(this);
            base.Initialize(options);
            Task.Run(RunAsync, StopToken);
        }

        public virtual void Dispose()
        {
            if (StopToken.IsCancellationRequested)
                return;
            try {
                _stopCts.Cancel();
            }
            catch {
                _stopCts.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        protected virtual async Task RunAsync()
        {
            var cancellationToken = StopToken;
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    var snapshot = Snapshot;
                    var computed = snapshot.Computed;
                    await computed.WhenInvalidatedAsync(cancellationToken).ConfigureAwait(false);
                    if (snapshot.UpdateCount != 0 || DelayFirstUpdate)
                        await UpdateDelayer.DelayAsync(this, cancellationToken).ConfigureAwait(false);
                    await computed.UpdateAsync(false, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    // Will break from while if it's due to cancellationToken cancellation
                }
                catch (Exception e) {
                    Log.LogError(e, $"Error in LiveState.RunAsync().");
                }
            }
        }
    }

    public abstract class LiveState<T, TOwn> : LiveState<T>, ILiveState<T, TOwn>
    {
        public new class Options : LiveState<T>.Options
        {
            public MutableState<TOwn>.Options OwnStateOptions { get; set; } = new MutableState<TOwn>.Options();
            public bool InvalidateOnOwnStateUpdate { get; set; } = true;
            public bool UpdateOnOwnStateUpdate { get; set; } = true;
        }

        public bool InvalidateOnOwnStateUpdate { get; set; }
        public bool UpdateOnOwnStateUpdate { get; set; }
        public IMutableState<TOwn> OwnState { get; }

        protected LiveState(
            Options options, IServiceProvider serviceProvider,
            object? argument = null, bool initialize = true)
            : base(options, serviceProvider, argument, false)
        {
            InvalidateOnOwnStateUpdate = options.InvalidateOnOwnStateUpdate;
            UpdateOnOwnStateUpdate = options.UpdateOnOwnStateUpdate;
            OwnState = new MutableState<TOwn>(options.OwnStateOptions, serviceProvider, default, this);
            OwnState.Updated += OwnStateUpdated;
            if (initialize) Initialize(options);
        }

        protected virtual void OwnStateUpdated(IState<TOwn> ownState)
        {
            if (UpdateOnOwnStateUpdate || InvalidateOnOwnStateUpdate)
                this.Invalidate(UpdateOnOwnStateUpdate);
        }
    }
}

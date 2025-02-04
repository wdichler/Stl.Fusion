using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stl.Plugins.Metadata;

namespace Stl.Plugins.Internal
{
    public class PredefinedPluginFinder : IPluginFinder
    {
        public class Options
        {
            public IEnumerable<Type> PluginTypes { get; set; } = Enumerable.Empty<Type>();
        }

        public PluginSetInfo FoundPlugins { get; }

        public PredefinedPluginFinder(Options options, IPluginInfoProvider pluginInfoProvider)
        {
            var pluginTypes = new HashSet<Type>(options.PluginTypes);
            FoundPlugins = new PluginSetInfo(pluginTypes, pluginInfoProvider);
        }

        public Task Run(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

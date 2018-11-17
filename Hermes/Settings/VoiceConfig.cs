using System.Collections;
using System.Collections.Generic;

namespace Hermes.Settings
{
    public class VoiceConfig
    {
        public IDictionary<string, IList<string>> Mapping { get; set; }
        public IList<string> Undecided { get; set; }
        public IList<string> Ending { get; set; }
        public IList<string> Start { get; set; }
        public string TransferTo { get; set; }
    }
}
using System.Collections;
using System.Collections.Generic;

namespace Hermes.Models
{
    public class CallState
    {
        public string Intent { get; set; }
        public CurrentCallState CurrentCallState { get; set; } = CurrentCallState.UnknownIntent;
        public List<string> AvailableResponse { get; set; } = new List<string>();
    }

    public enum CurrentCallState
    {
        HungUp,
        Transferred,
        UnknownIntent,
        ConfirmedIntent
    }
}
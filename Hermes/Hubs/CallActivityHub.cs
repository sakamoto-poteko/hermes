using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace Hermes.Hubs
{    
    public class CallActivityHub : Hub
    {
        public async Task SendSpeech(string cid, string content)
        {
            await Clients.All.SendAsync("SendSpeech", cid, content);
        }

        public async Task SendAction(string cid, string action)
        {
            await Clients.All.SendAsync("SendAction", cid, action);
        }

        public async Task SendShortAction(string action)
        {
            await Clients.All.SendAsync("SendShortAction", action);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hermes.Models;
using Hermes.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.CognitiveServices.Language.LUIS.Runtime;
using Microsoft.Extensions.Logging;
using Twilio.AspNet.Core;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

namespace Hermes.Controllers
{
    [Route("voice")]
    public class VoiceController : TwilioController
    {
        private readonly ILogger<VoiceController> _logger;
        private readonly ILUISRuntimeClient _luisRuntimeClient;
        private readonly IDictionary<string, CallState> _callStates;
        private readonly VoiceConfig _voiceConfig;
        private readonly LuisSettings _luisSettings;

        public VoiceController(ILogger<VoiceController> logger, ILUISRuntimeClient luisRuntimeClient,
            IDictionary<string, CallState> callStates, VoiceConfig voiceConfig, LuisSettings luisSettings)
        {
            _logger = logger;
            _luisRuntimeClient = luisRuntimeClient;
            _callStates = callStates;
            _voiceConfig = voiceConfig;
            _luisSettings = luisSettings;
        }

        [Route("answer")]
        [HttpPost]
        [Consumes("application/x-www-form-urlencoded")]
        public IActionResult Answer([FromForm] string callSid, [FromForm] string from)
        {
            _callStates.Add(callSid, new CallState());

            _logger.LogInformation($"Incoming call from {from} with SID {callSid}");

            _logger.LogInformation("Call received");
            return TwiML(GatherPlayResponse("000000.wav"));
        }

        [Route("gatherresult")]
        [HttpPost]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> GatherResult([FromForm] string callSid, [FromForm] string speechResult)
        {
            _logger.LogDebug($"Full@{DateTime.Now.ToString()} {callSid}: {speechResult}");

            return await Dispatcher(callSid, speechResult);
        }

        private async Task<IActionResult> Dispatcher(string callSid, string inputText)
        {
            if (!_callStates.ContainsKey(callSid))
            {
                return BadRequest();
            }

            var state = _callStates[callSid];

            var (intent, score) = await LookupIntent(inputText);

            switch (state.CurrentCallState)
            {
                case CurrentCallState.HungUp:
                    // This phone is already hung up
                    _logger.LogWarning($"[{callSid}]Phone is already hung up");
                    return TwiML(TwiMlHangUp());
                case CurrentCallState.Transferred:
                    // This phone is already transferred
                    _logger.LogWarning($"[{callSid}]Phone is already transferred");
                    return TwiML(TwiMlHangUp());
                case CurrentCallState.UnknownIntent:
                    // The phone intent is unknown yet.
                    // Look up by LUIS first,
                    // if matches, switch to relevant intent and pin
                    // play corresponding voices then
                    // Else, play some useless voices
                    _logger.LogInformation($"[{callSid}]Current intent unknown, input {intent}, score {score.Value}");

                    if (score > 0.2 && IsEndingIntent(intent))
                    {
                        state.CurrentCallState = CurrentCallState.HungUp;
                        return TwiML(TwiMlHangUp());
                    }
                    else if (score > 0.5)
                    {
                        state.Intent = intent;

                        // Or is it an transfer intent?
                        if (intent == "None")
                        {
                            state.CurrentCallState = CurrentCallState.Transferred;
                            return TwiML(TwiMlTransfer());
                        }

                        // else
                        state.CurrentCallState = CurrentCallState.ConfirmedIntent;
                        return TwiML(TwiMlPlayIntentResponse(state.Intent, state));
                    }
                    else
                    {
                        return TwiML(TwiMlPlayUnknownIntent());
                    }
                case CurrentCallState.ConfirmedIntent:
                    // Filter ending intent
                    _logger.LogInformation(
                        $"[{callSid}]Current intent {state.Intent}, input {intent}, score {score.Value}");
                    if (score > 0.2 && IsEndingIntent(intent))
                    {
                        state.CurrentCallState = CurrentCallState.HungUp;
                        return TwiML(TwiMlHangUp());
                    }

                    return TwiML(TwiMlPlayIntentResponse(state.Intent, state));
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private VoiceResponse TwiMlTransfer()
        {
            _logger.LogDebug("Transferring call");

            if (!_voiceConfig.Mapping.ContainsKey("None"))
            {
                throw new InvalidConfigException("Transfer voices not found in config");
            }

            var voices = _voiceConfig.Mapping["None"];
            var idx = new Random().Next(voices.Count);

            var response = new VoiceResponse();
            response.Play(GetVoiceUrl(voices[idx]));
            response.Dial(_voiceConfig.TransferTo);
            return response;
        }

        private bool IsEndingIntent(string intent)
        {
            return _voiceConfig.Ending.Any(s => s == intent);
        }

        private VoiceResponse TwiMlPlayUnknownIntent()
        {
            _logger.LogDebug("Playing unknown intent voices");
            // Get voices list
            var subjects = _voiceConfig.Undecided;
            var voices = new List<string>();
            foreach (var subject in subjects)
            {
                if (_voiceConfig.Mapping.ContainsKey(subject))
                {
                    voices.AddRange(_voiceConfig.Mapping[subject]);
                }
            }

            var voice = voices[new Random().Next(voices.Count)];

            return GatherPlayResponse(voice);
        }

        private VoiceResponse GatherPlayResponse(string voice)
        {
            _logger.LogDebug($"Play interactive voice response {voice}");
            var response = new VoiceResponse();

            // response.Play(GetVoiceUrl(voice));

            var gather = new Gather(language: Gather.LanguageEnum.CmnHansCn,
                action: new Uri("/voice/gatherresult", UriKind.Relative),
                input: new List<Gather.InputEnum>() {Gather.InputEnum.Speech},
                speechTimeout: "auto"
            );
            gather.Play(GetVoiceUrl(voice));

            response.Append(gather);

            return response;
        }

        private Uri GetVoiceUrl(string voice)
        {
            _logger.LogDebug($"Retrieved url for voice {voice}");
            return new Uri($"https://hermescalling.blob.core.windows.net/voice/{voice}");
        }

        private VoiceResponse TwiMlPlayIntentResponse(string intent, CallState state)
        {
            _logger.LogDebug($"Play response for intent {intent}");

            if (state.AvailableResponse.Count == 0)
            {
                // Fill list    
                // Get voices list
                if (_voiceConfig.Mapping.ContainsKey(intent))
                {
                    state.AvailableResponse.AddRange(_voiceConfig.Mapping[intent]);
                }
                else
                {
                    throw new InvalidConfigException($"Intent {intent} not found in voice mapping");
                }
            }

            var voice = state.AvailableResponse[0];
            state.AvailableResponse.RemoveAt(0);

            return GatherPlayResponse(voice);
        }

        private VoiceResponse TwiMlHangUp()
        {
            _logger.LogDebug("Hang up the phone");
            var hangUpVoices = new List<string>();

            foreach (var endingCat in _voiceConfig.Ending)
            {
                if (_voiceConfig.Mapping.ContainsKey(endingCat))
                {
                    hangUpVoices.AddRange(_voiceConfig.Mapping[endingCat]);
                }
            }

            if (hangUpVoices.Count == 0)
            {
                throw new InvalidConfigException("No ending voice configured");
            }

            var idx = new Random().Next(hangUpVoices.Count);

            var response = new VoiceResponse();
            response.Play(GetVoiceUrl(hangUpVoices[idx]));
            response.Hangup();
            return response;
        }

        [Route("gatherresultpartial")]
        [HttpPost]
        [Consumes("application/x-www-form-urlencoded")]
        public IActionResult GatherResultPartial([FromForm] string callSid, [FromForm] string unstableSpeechResult)
        {
            _logger.LogDebug($"Partial@{DateTime.Now.ToString()} {callSid}: {unstableSpeechResult}");
            return TwiML(new VoiceResponse());
        }

        [Route("intent")]
        [HttpGet]
        public async Task<IActionResult> GetIntent([FromQuery] string input)
        {
            var (intent, score) = await LookupIntent(input);
            return Ok(new
            {
                Intent = intent,
                Score = score
            });
        }

        private async Task<(string intent, double? score)> LookupIntent(string input)
        {
            var result =
                await _luisRuntimeClient.Prediction.ResolveAsync(_luisSettings.ApplicationId, input);
            return (result.TopScoringIntent.Intent, result.TopScoringIntent.Score);
        }
    }
}
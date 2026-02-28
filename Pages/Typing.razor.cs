using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Threading;

namespace TestTypingSpeeds1.Pages
{
    public partial class Typing : ComponentBase, IDisposable
    {
        // Setup
        public bool IsTestActive { get; set; }
        public bool IsFinished { get; set; }
        public int SelectedTimeOption { get; set; } = 30;
        public string SelectedDifficulty { get; set; } = "Easy";

        // Timer
        public int DurationSeconds { get; set; }
        private int _elapsedSeconds;
        private DateTime? _startUtc;
        private CancellationTokenSource? _cts;
        public int SecondsLeft => Math.Max(0, DurationSeconds - _elapsedSeconds);

        // Lines
        public List<string> Lines { get; } = new();
        private readonly Dictionary<int, string> _typedByLine = new();
        public int CurrentLineIndex { get; set; }
        public string CurrentTyped { get; set; } = "";
        protected ElementReference CurrentInputRef;

        private bool _focusNextRender;

        // Results
        public int CorrectChars { get; set; }
        public int TotalTypedChars { get; set; }
        public int Wpm { get; set; }
        public int Accuracy { get; set; }

        //Danga 2/28/2026 
        //Added 
        [Inject]
        private HttpClient Http { get; set; } = default!;

        private readonly Dictionary<string, List<string>> _phraseCache = new();
        private static readonly Random _random = new();
        private string? _lastPhrase;

        public async Task StartTest()
        {
            IsTestActive = true;
            DurationSeconds = SelectedTimeOption;

            ResetState();

            var fullText = await GetPhraseAsync(SelectedDifficulty);

            Lines.Clear();
            Lines.AddRange(WrapIntoLines(fullText, 45));

            _focusNextRender = true;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (_focusNextRender && !IsFinished && IsTestActive)
            {
                _focusNextRender = false;
                await CurrentInputRef.FocusAsync();
            }
        }

        public void OnCurrentInput(ChangeEventArgs e)
        {
            CurrentTyped = e.Value?.ToString() ?? "";

            if (_startUtc is null && !IsFinished && CurrentTyped.Length > 0)
                _ = StartCountdownAsync();
        }

        public void OnKeyDown(KeyboardEventArgs e)
        {
            if (IsFinished) return;
            if (CurrentLineIndex >= Lines.Count) return;

            if (e.Key == "Enter")
            {
                CommitLine();
                return;
            }

            var target = Lines[CurrentLineIndex];
            if (CurrentTyped.Length == target.Length)
                CommitLine();
        }

        private void CommitLine()
        {
            if (CurrentLineIndex >= Lines.Count) return;

            _typedByLine[CurrentLineIndex] = CurrentTyped;

            CurrentLineIndex++;
            CurrentTyped = "";

            if (CurrentLineIndex >= Lines.Count)
            {
                FinishTest();
                return;
            }

            _focusNextRender = true;
            StateHasChanged();
        }

        public string GetTypedForLine(int index)
            => _typedByLine.TryGetValue(index, out var t) ? t : "";

        private async Task StartCountdownAsync()
        {
            if (_startUtc is not null || IsFinished) return;

            _startUtc = DateTime.UtcNow;
            _elapsedSeconds = 0;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                while (await timer.WaitForNextTickAsync(token))
                {
                    _elapsedSeconds++;

                    if (_elapsedSeconds >= DurationSeconds)
                    {
                        FinishTest();
                        break;
                    }

                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void FinishTest()
        {
            if (IsFinished) return;

            IsFinished = true;
            _cts?.Cancel();

            ComputeResults();
            _ = InvokeAsync(StateHasChanged);
        }

        private void ComputeResults()
        {
            var typedLines = _typedByLine.OrderBy(k => k.Key).Select(k => k.Value).ToList();
            if (!string.IsNullOrEmpty(CurrentTyped))
                typedLines.Add(CurrentTyped);

            var typedAll = string.Join(" ", typedLines);
            var targetAll = string.Join(" ", Lines);

            TotalTypedChars = typedAll.Length;
            CorrectChars = CountCorrectChars(typedAll, targetAll);

            Accuracy = TotalTypedChars == 0
                ? 0
                : (int)Math.Round((double)CorrectChars / TotalTypedChars * 100);

            double minutes = DurationSeconds / 60.0;
            double grossWpm = (TotalTypedChars / 5.0) / minutes;
            int errors = Math.Max(0, TotalTypedChars - CorrectChars);
            double penalty = (errors / 5.0) / minutes;

            Wpm = (int)Math.Round(Math.Max(0, grossWpm - penalty));
        }

        private void ResetState()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _startUtc = null;
            _elapsedSeconds = 0;
            IsFinished = false;

            _typedByLine.Clear();
            CurrentLineIndex = 0;
            CurrentTyped = "";

            CorrectChars = 0;
            TotalTypedChars = 0;
            Wpm = 0;
            Accuracy = 0;
        }

        public Task ResetTest()
        {
            ResetState();
            _focusNextRender = true;
            return Task.CompletedTask;
        }

        public Task BackToSetup()
        {
            ResetState();
            IsTestActive = false;
            return Task.CompletedTask;
        }

        private static int CountCorrectChars(string typed, string target)
        {
            int correct = 0;
            int len = Math.Min(typed.Length, target.Length);
            for (int i = 0; i < len; i++)
                if (typed[i] == target[i]) correct++;
            return correct;
        }

        private static IEnumerable<string> WrapIntoLines(string text, int maxLineChars)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = new List<string>();
            int len = 0;

            foreach (var w in words)
            {
                int add = (line.Count == 0 ? w.Length : w.Length + 1);
                if (len + add > maxLineChars && line.Count > 0)
                {
                    yield return string.Join(" ", line);
                    line.Clear();
                    len = 0;
                }

                line.Add(w);
                len += add;
            }

            if (line.Count > 0)
                yield return string.Join(" ", line);
        }

        //private static string GetPhrase(string difficulty)
        //{
        //    return difficulty switch
        //    {
        //        "Easy" => "The cat jumped over the mouse and fox which is easy.",
        //        "Medium" => "Typing fast requires focus and consistent daily practice so your hands learn the rhythm of words.",
        //        _ => "Pack my box with five dozen liquor jugs, then quickly judge my vow as the wizard of quizzical puzzles watches."
        //    };
        //}


        private async Task<string> GetPhraseAsync(string difficulty)
        {
            if (!_phraseCache.ContainsKey(difficulty))
            {
                var phrases = await LoadPhrasesAsync(difficulty);
                _phraseCache[difficulty] = phrases;
            }

            var list = _phraseCache[difficulty];

            if (list == null || list.Count == 0)
                list = GetFallbackPhrases(difficulty);

            string selected;
            do
            {
                selected = list[_random.Next(list.Count)];
            }
            while (list.Count > 1 && selected == _lastPhrase);

            _lastPhrase = selected;

            return selected;
        }

        private async Task<List<string>> LoadPhrasesAsync(string difficulty)
        {
            try
            {
                var content = await Http.GetStringAsync($"phrases/{difficulty}.txt");

                var phrases = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(l => l.Trim())
                                     .Where(l => !string.IsNullOrWhiteSpace(l))
                                     .ToList();

                return phrases.Count > 0
                    ? phrases
                    : GetFallbackPhrases(difficulty);
            }
            catch
            {
                return GetFallbackPhrases(difficulty);
            }
        }

        // ── Fallback if text files are missing ────────────────────────────────────
        private static List<string> GetFallbackPhrases(string difficulty) => difficulty switch
        {
            "Easy" => new List<string>
            {
                "The cat jumped over the mouse and the fox which is easy."
            },
                    "Medium" => new List<string>
            {
                "Typing fast requires focus and consistent daily practice so your hands learn the rhythm of words."
            },
                    _ => new List<string>
            {
                "Pack my box with five dozen liquor jugs then quickly judge my vow as the wizard of quizzical puzzles watches."
            }
        };


        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
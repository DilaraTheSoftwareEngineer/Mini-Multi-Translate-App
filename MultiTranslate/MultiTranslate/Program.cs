using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MultiTranslate
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
           
            Application.Run(new TranslatorForm());
        }
    }

    public class TranslatorForm : Form
    {
        readonly TextBox txtInput = new TextBox();
        readonly ComboBox cmbFrom = new ComboBox();
        readonly ComboBox cmbTo = new ComboBox();
        readonly Button btnTranslate = new Button();
        readonly TextBox txtOutput = new TextBox();
        readonly Button btnCopy = new Button();
        readonly Label lblStatus = new Label();
        readonly HttpClient http = new HttpClient();

        // Basit dil listesi (istediğin kadar ekleyebilirsin)
        readonly Dictionary<string, string> languages = new()
        {
            {"Auto Detect", "auto"},
            {"English", "en"},
            {"Turkish", "tr"},
            {"Spanish", "es"},
            {"French", "fr"},
            {"German", "de"},
            {"Italian", "it"},
            {"Portuguese", "pt"},
            {"Russian", "ru"},
            {"Arabic", "ar"},
            {"Chinese (Simplified)", "zh"},
            {"Japanese", "ja"},
            {"Korean", "ko"}
        };

        public TranslatorForm()
        {
            Text = "Multi-Translate (.NET 8 WinForms)";
            Width = 820;
            Height = 560;
            StartPosition = FormStartPosition.CenterScreen;

            // Input TextBox
            txtInput.Multiline = true;
            txtInput.ScrollBars = ScrollBars.Vertical;
            txtInput.SetBounds(10, 10, 520, 200);
            txtInput.Font = new System.Drawing.Font("Segoe UI", 10);

            // From combobox
            cmbFrom.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFrom.SetBounds(540, 10, 240, 28);

            // To combobox
            cmbTo.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbTo.SetBounds(540, 50, 240, 28);

            // Translate button
            btnTranslate.Text = "Translate ▶";
            btnTranslate.SetBounds(540, 90, 240, 40);
            btnTranslate.Click += async (_, __) => await OnTranslateClicked();

            // Output TextBox
            txtOutput.Multiline = true;
            txtOutput.ScrollBars = ScrollBars.Vertical;
            txtOutput.ReadOnly = true;
            txtOutput.SetBounds(10, 220, 770, 260);
            txtOutput.Font = new System.Drawing.Font("Segoe UI", 10);

            // Copy button
            btnCopy.Text = "Copy Result";
            btnCopy.SetBounds(10, 486, 120, 28);
            btnCopy.Click += (_, __) => { Clipboard.SetText(txtOutput.Text ?? ""); };

            // Status label
            lblStatus.SetBounds(140, 486, 540, 28);

            Controls.AddRange(new Control[] { txtInput, cmbFrom, cmbTo, btnTranslate, txtOutput, btnCopy, lblStatus });

            // Fill language combos
            foreach (var kv in languages)
            {
                cmbFrom.Items.Add(kv.Key);
                cmbTo.Items.Add(kv.Key);
            }
            cmbFrom.SelectedIndex = 0; // Auto Detect
            cmbTo.SelectedIndex = 1;    // English

            // If user set custom endpoint via env var, show short note
            var customUrl = Environment.GetEnvironmentVariable("TRANSLATE_API_URL");
            if (!string.IsNullOrEmpty(customUrl))
            {
                lblStatus.Text = $"Using custom endpoint: {customUrl}";
            }
        }

        async Task OnTranslateClicked()
        {
            var text = txtInput.Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                MessageBox.Show("Please enter text to translate.", "Empty input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var fromKey = languages[(string)cmbFrom.SelectedItem];
            var toKey = languages[(string)cmbTo.SelectedItem];

            btnTranslate.Enabled = false;
            lblStatus.Text = "Translating...";
            txtOutput.Text = "";

            try
            {
                var result = await TranslateTextAsync(text, fromKey, toKey);
                txtOutput.Text = result;
                lblStatus.Text = "Done.";
            }
            catch (Exception ex)
            {
                txtOutput.Text = "";
                lblStatus.Text = "Error: " + ex.Message;
                MessageBox.Show("Translation failed:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnTranslate.Enabled = true;
            }
        }

        /// <summary>
        /// Generic translation method:
        /// - If TRANSLATE_API_URL env var is set, will POST there with JSON { q, source, target, format }
        ///   and will add "Authorization: Bearer {TRANSLATE_API_KEY}" header if TRANSLATE_API_KEY exists.
        /// - Otherwise falls back to libretranslate.de public endpoint.
        /// </summary>
        async Task<string> TranslateTextAsync(string text, string sourceLang, string targetLang)
        {
            // If user chose "auto", send "auto" and let service detect
            string source = sourceLang == "auto" ? "auto" : sourceLang;

            var customUrl = Environment.GetEnvironmentVariable("TRANSLATE_API_URL");
            var customKey = Environment.GetEnvironmentVariable("TRANSLATE_API_KEY");

            string endpoint;
            bool useLibre = false;
            if (!string.IsNullOrEmpty(customUrl))
            {
                endpoint = customUrl;
            }
            else
            {
                // fallback to public libretranslate instance - HATA BURADA DÜZELTİLDİ:
                endpoint = "https://translate.argosopentech.com/translate"; // <-- Doğru URL
                useLibre = true;
            }

            var payload = new Dictionary<string, object>
            {
                {"q", text},
                {"source", source},
                {"target", targetLang},
                {"format", "text"}
            };

            var json = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrEmpty(customKey))
            {
                // Common pattern: Authorization header with Bearer token (works for many APIs)
                req.Headers.Add("Authorization", "Bearer " + customKey);
            }
            else if (useLibre)
            {
                // libretranslate may require "accept" header but not required here.
            }

            using var resp = await http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"API returned {(int)resp.StatusCode}: {body}");
            }

            // Try to parse a few common response shapes:
            // LibreTranslate -> { "translatedText": "..." }
            // Some APIs -> { "data": { "translations": [ { "translatedText": "..." } ] } }
            // If response is plain string, return it.

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("translatedText", out var txtEl))
                {
                    return txtEl.GetString() ?? "";
                }
                if (root.TryGetProperty("data", out var dataEl) &&
                    dataEl.TryGetProperty("translations", out var transEl) &&
                    transEl.ValueKind == JsonValueKind.Array &&
                    transEl.GetArrayLength() > 0)
                {
                    var first = transEl[0];
                    if (first.TryGetProperty("translatedText", out var tt))
                        return tt.GetString() ?? "";
                    if (first.TryGetProperty("translation", out var t2))
                        return t2.GetString() ?? "";
                }

                // Fallback: if body is just a string or has "result"
                if (root.ValueKind == JsonValueKind.String)
                    return root.GetString() ?? "";

                if (root.TryGetProperty("result", out var r2) && r2.ValueKind == JsonValueKind.String)
                    return r2.GetString() ?? "";

                // As a last resort, return raw JSON
                return body;
            }
            catch (JsonException)
            {
                // Not JSON — return raw
                return body;
            }
        }
    }
}
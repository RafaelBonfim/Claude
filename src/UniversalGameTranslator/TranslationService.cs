using System;
using System.Collections.Generic;
using System.Linq; // <--- ADICIONADO PARA CORRIGIR ERROS DE 'OrderBy' e 'ToList'
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using UniversalGameTranslator.Models;

namespace UniversalGameTranslator.Services
{
    public class TranslationService
    {
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, string> _cache;
        private readonly Dictionary<string, DateTime> _cacheTimestamps;
        private const int CACHE_EXPIRY_HOURS = 24;
        private const int MAX_CACHE_SIZE = 1000;

        public TranslationService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _cache = new Dictionary<string, string>();
            _cacheTimestamps = new Dictionary<string, DateTime>();

            // Set user agent to avoid blocking
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        public async Task<string> TranslateAsync(string text, string targetLanguage, string sourceLanguage = "auto")
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var request = new TranslationRequest
            {
                Text = text,
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage
            };

            var response = await TranslateAsync(request);
            return response.Success ? response.TranslatedText : text;
        }

        public async Task<TranslationResponse> TranslateAsync(TranslationRequest request)
        {
            var startTime = DateTime.Now;

            try
            {
                // Check cache first
                var cacheKey = $"{request.SourceLanguage}:{request.TargetLanguage}:{request.Text}";
                if (IsInCache(cacheKey))
                {
                    return new TranslationResponse
                    {
                        TranslatedText = _cache[cacheKey],
                        Success = true,
                        ProcessingTime = DateTime.Now - startTime,
                        RequestId = request.RequestId
                    };
                }

                // Try multiple translation services
                var result = await TryGoogleTranslate(request) ??
                           await TryBingTranslate(request) ??
                           await TryLibreTranslate(request);

                if (result != null && result.Success)
                {
                    // Cache successful translation
                    AddToCache(cacheKey, result.TranslatedText);
                    result.ProcessingTime = DateTime.Now - startTime;
                    return result;
                }

                // Fallback - return original text
                return new TranslationResponse
                {
                    TranslatedText = request.Text,
                    Success = false,
                    ErrorMessage = "All translation services failed",
                    ProcessingTime = DateTime.Now - startTime,
                    RequestId = request.RequestId
                };
            }
            catch (Exception ex)
            {
                return new TranslationResponse
                {
                    TranslatedText = request.Text,
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTime = DateTime.Now - startTime,
                    RequestId = request.RequestId
                };
            }
        }

        private async Task<TranslationResponse?> TryGoogleTranslate(TranslationRequest request) // <--- CORRIGIDO: Adicionado '?'
        {
            try
            {
                // Google Translate free API
                var encodedText = HttpUtility.UrlEncode(request.Text);
                var url = $"https://translate.googleapis.com/translate_a/single?" +
                         $"client=gtx&sl={request.SourceLanguage}&tl={request.TargetLanguage}&dt=t&q={encodedText}";

                var response = await _httpClient.GetStringAsync(url);
                var jsonDoc = JsonDocument.Parse(response);

                var translatedText = new StringBuilder();
                var translations = jsonDoc.RootElement[0].EnumerateArray();

                foreach (var translation in translations)
                {
                    if (translation.ValueKind == JsonValueKind.Array && translation.GetArrayLength() > 0)
                    {
                        var text = translation[0].GetString();
                        if (!string.IsNullOrEmpty(text))
                            translatedText.Append(text);
                    }
                }

                var result = translatedText.ToString().Trim();
                if (string.IsNullOrEmpty(result))
                    return null;

                return new TranslationResponse
                {
                    TranslatedText = result,
                    Success = true,
                    RequestId = request.RequestId
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Google Translate failed: {ex.Message}");
                return null;
            }
        }

        private async Task<TranslationResponse?> TryBingTranslate(TranslationRequest request) // <--- CORRIGIDO: Adicionado '?'
        {
            try
            {
                // Bing Translator free endpoint
                var encodedText = HttpUtility.UrlEncode(request.Text);
                var url = $"https://www.bing.com/ttranslatev3?" +
                         $"isVertical=1&IG=&IID=translator.5023.3" +
                         $"&fromLang={request.SourceLanguage}&to={request.TargetLanguage}&text={encodedText}";

                var response = await _httpClient.GetStringAsync(url);

                // Simple parsing for Bing response (this is a simplified approach)
                if (response.Contains("translationText"))
                {
                    var startIndex = response.IndexOf("\"translationText\":\"") + 19;
                    var endIndex = response.IndexOf("\"", startIndex);

                    if (startIndex > 18 && endIndex > startIndex)
                    {
                        var translatedText = response.Substring(startIndex, endIndex - startIndex);
                        translatedText = System.Text.RegularExpressions.Regex.Unescape(translatedText);

                        return new TranslationResponse
                        {
                            TranslatedText = translatedText,
                            Success = true,
                            RequestId = request.RequestId
                        };
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bing Translate failed: {ex.Message}");
                return null;
            }
        }

        private async Task<TranslationResponse?> TryLibreTranslate(TranslationRequest request) // <--- CORRIGIDO: Adicionado '?'
        {
            try
            {
                // LibreTranslate public instance (if available)
                var url = "https://libretranslate.de/translate";
                var payload = new
                {
                    q = request.Text,
                    source = request.SourceLanguage == "auto" ? "en" : request.SourceLanguage,
                    target = request.TargetLanguage,
                    format = "text"
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseText = await response.Content.ReadAsStringAsync();

                var jsonDoc = JsonDocument.Parse(responseText);
                var translatedText = jsonDoc.RootElement.GetProperty("translatedText").GetString();

                if (translatedText == null) return null; // <--- CORRIGIDO: Verificação de nulo

                return new TranslationResponse
                {
                    TranslatedText = translatedText,
                    Success = true,
                    RequestId = request.RequestId
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LibreTranslate failed: {ex.Message}");
                return null;
            }
        }

        private bool IsInCache(string key)
        {
            if (!_cache.ContainsKey(key)) return false;

            // Check if cache entry is still valid
            if (_cacheTimestamps.TryGetValue(key, out DateTime timestamp))
            {
                if (DateTime.Now - timestamp > TimeSpan.FromHours(CACHE_EXPIRY_HOURS))
                {
                    _cache.Remove(key);
                    _cacheTimestamps.Remove(key);
                    return false;
                }
            }

            return true;
        }

        private void AddToCache(string key, string value)
        {
            // Clean cache if too large
            if (_cache.Count >= MAX_CACHE_SIZE)
            {
                CleanOldCacheEntries();
            }

            _cache[key] = value;
            _cacheTimestamps[key] = DateTime.Now;
        }

        private void CleanOldCacheEntries()
        {
            var cutoffTime = DateTime.Now.AddHours(-CACHE_EXPIRY_HOURS);
            var keysToRemove = new List<string>();

            foreach (var kvp in _cacheTimestamps)
            {
                if (kvp.Value < cutoffTime)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
                _cacheTimestamps.Remove(key);
            }

            // If still too large, remove oldest entries
            if (_cache.Count >= MAX_CACHE_SIZE)
            {
                var oldestEntries = _cacheTimestamps
                    .OrderBy(kvp => kvp.Value)
                    .Take(_cache.Count - MAX_CACHE_SIZE / 2)
                    .Select(kvp => kvp.Key);

                foreach (var key in oldestEntries.ToList())
                {
                    _cache.Remove(key);
                    _cacheTimestamps.Remove(key);
                }
            }
        }

        public void ClearCache()
        {
            _cache.Clear();
            _cacheTimestamps.Clear();
        }

        public int GetCacheSize()
        {
            return _cache.Count;
        }
    }
}
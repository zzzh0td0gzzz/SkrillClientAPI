﻿using Newtonsoft.Json;
using SkrillClientAPI.Controllers.Models;
using SkrillClientAPI.Services.Models;
using System.Globalization;
using System.Net;
using System.Text;

namespace SkrillClientAPI.Services
{
    public class SkrillAPIService
    {
        ClientRequest clientRequest;

        public SkrillAPIService(ClientRequest client)
        {
            this.clientRequest = client;
        }

        public async Task<HttpStatusCode> RegisterSkrillSession()
        {
            var url = "https://account.skrill.com/api/login/session-register";
            var query = "redirect_uri=https://account.skrill.com/wallet/account/assets/html/auth.html";
            var response = await clientRequest.Client.GetAsync($"{url}?{query}");
            return response.StatusCode;
        }

        public async Task<HttpStatusCode> AuthorizeSkrillSession()
        {
            clientRequest.Client.DefaultRequestHeaders.Clear();
            var cookies = clientRequest.Cookies.GetCookies(clientRequest.Uri);
            var cookiesToString = cookies.Select(item => $"{item.Name}={item.Value}").ToList();
            clientRequest.Client.DefaultRequestHeaders.Add("cookie", String.Join(";", cookiesToString));
            var url = "https://account.skrill.com/api/login/authorize";
            var response = await clientRequest.Client.GetAsync(url);
            return response.StatusCode;
        }

        public async Task<string> Login(UserModel user, bool requireOTP = false)
        {
            var captchaCode = await CaptchaService.ResolveCaptcha(clientRequest);
            clientRequest.CaptchaResolvedCode = captchaCode;
            clientRequest.User = user;

            clientRequest.SetHeader();

            var captchaValidationRequest = new CaptchaValidationRequest();
            captchaValidationRequest.response = clientRequest.CaptchaResolvedCode;
            captchaValidationRequest.siteKey = clientRequest.SiteKey;

            var loginModel = new LoginModel();
            loginModel.captchaValidationRequest = captchaValidationRequest;
            loginModel.username = clientRequest.User.Username;
            loginModel.password = clientRequest.User.Password;

            var valuesJson = JsonConvert.SerializeObject(loginModel);
            var content = new StringContent(valuesJson, Encoding.UTF8, "application/json");
            var url = "https://account.skrill.com/api/login";
            var response = await clientRequest.Client.PostAsync(url, content);
            if (!requireOTP)
                return await response.Content.ReadAsStringAsync();

            var responseString = await response.Content.ReadAsStringAsync();
            dynamic responseObject = JsonConvert.DeserializeObject(responseString);
            string links = responseObject.links[0].href;
            var data = links.Split('?')[1];
            var item1 = data.Split('&')[0];
            var item2 = data.Split('&')[1];

            if (item1.ToLower().Contains("eventid"))
            {
                clientRequest.EventId = item1.Split('=')[1];
                clientRequest.ClientEventId = item2.Split('=')[1];
            }
            else
            {
                clientRequest.EventId = item2.Split('=')[1];
                clientRequest.ClientEventId = item1.Split('=')[1];
            }
            return await RequestOTP();
        }

        public async Task<string> RequestOTP()
        {
            clientRequest.SetHeader();
            Dictionary<string, string> values = new Dictionary<string, string>()
            {
                {"eventId", clientRequest.EventId},
                {"clientEventId", clientRequest.ClientEventId }
            };
            var valuesJson = JsonConvert.SerializeObject(values);
            var content = new StringContent(valuesJson, Encoding.UTF8, "application/json");
            var url = "https://account.skrill.com/api/2fa/v1/sms-challenge";
            var result = await clientRequest.Client.PostAsync(url, content);
            return await result.Content.ReadAsStringAsync();
        }

        public async Task<string> SubmitOTPToLogin(string otp)
        {
            clientRequest.SetHeader();
            Dictionary<string, string?> values = new Dictionary<string, string?>()
            {
                {"clientEventId", clientRequest.ClientEventId },
                {"eventId", clientRequest.EventId },
                {"merchantPartyId", null },
                {"otpCodeType", "sms" },
                {"tmxSessionId", clientRequest.SessionKey.ToString() },
                {"verifyCode", otp }
            };
            var valuesJson = JsonConvert.SerializeObject(values);
            var content = new StringContent(valuesJson, Encoding.UTF8, "application/json");
            var url = "https://account.skrill.com/api/2fa/v1/otp-verify";
            await clientRequest.Client.PostAsync(url, content);
            return await LoginAfterSubmitOTP();
        }

        public async Task<string> LoginAfterSubmitOTP()
        {
            clientRequest.SetHeader();

            var captchaValidationRequest = new CaptchaValidationRequest();
            captchaValidationRequest.response = clientRequest.CaptchaResolvedCode;
            captchaValidationRequest.siteKey = clientRequest.SiteKey;

            var loginWithOTPModel = new LoginWithOTPModel();
            loginWithOTPModel.captchaValidationRequest = captchaValidationRequest;
            loginWithOTPModel.username = clientRequest.User.Username;
            loginWithOTPModel.password = clientRequest.User.Password;
            loginWithOTPModel.scaEventId = clientRequest.EventId;
            loginWithOTPModel.scaClientEventId = clientRequest.ClientEventId;

            var valuesJson = JsonConvert.SerializeObject(loginWithOTPModel);
            var content = new StringContent(valuesJson, Encoding.UTF8, "application/json");
            var url = "https://account.skrill.com/api/login";
            var response = await clientRequest.Client.PostAsync(url, content);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<MoneyRequestModel> CreateMoneyRequest(MoneyRequestCreateModel request)
        {
            clientRequest.SetHeader();

            var valuesJson = JsonConvert.SerializeObject(request);
            var content = new StringContent(valuesJson, Encoding.UTF8, "application/json");
            var url = "https://account.skrill.com/api/p2p/v1/request-money";
            var response = await clientRequest.Client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            dynamic result = JsonConvert.DeserializeObject(responseContent);
            MoneyRequestModel moneyRequest = new MoneyRequestModel()
            {
                id = result.id.ToString(),
                key = result.key.ToString()
            };
            return moneyRequest;
        }

        public async Task<string> DeleteMoneyRequest(string id)
        {
            clientRequest.SetHeader();
            var url = $"https://account.skrill.com/api/p2p/v1/request-money/{id}/cancel";
            var response = await clientRequest.Client.PutAsync(url, null);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<bool> IsExpiredMoneyRequest(string key)
        {
            clientRequest.SetHeader();
            var url = $"https://account.skrill.com/api/p2p/v1/request-money/keys/{key}/preview";
            var response = await clientRequest.Client.GetAsync(url);
            var responseContent = await response.Content.ReadAsStringAsync();
            return responseContent.Contains("REQUEST_MONEY_LINK_EXPIRED");
        }

        public async Task<string> CheckMoneyRequest(string currency, string fromdate, string todate, int page, int pagesize)
        {
            clientRequest.SetHeader();
            var fromDateUTC = DateTime.ParseExact(fromdate, "dd-MM-yyyy", CultureInfo.InvariantCulture).ToString("s");
            var toDateUTC = DateTime.ParseExact(todate, "dd-MM-yyyy", CultureInfo.InvariantCulture).ToString("s");
            var url = $"https://account.skrill.com/api/wallet/v1/transactions-history";
            var query = $"currency={currency}&fromDateUtc={fromDateUTC}&toDateUtc={toDateUTC}&page={page}&countPerPage={pagesize}";
            var response = await clientRequest.Client.GetAsync($"{url}?{query}");
            var responseContent = await response.Content.ReadAsStringAsync();
            return responseContent;
        }
    }
}
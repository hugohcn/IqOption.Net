﻿using System;
using System.Net;
using System.Threading.Tasks;
using IqOptionApi.Extensions;
using IqOptionApi.http.Commands;
using IqOptionApi.Logging;
using IqOptionApi.Models;
using ReactiveUI;
using RestSharp;

namespace IqOptionApi.http {
    public class IqHttpClient : ReactiveObject, IIqHttpClient {
        private readonly ILog _logger = LogProvider.GetLogger("[HTTPS]");


        private Profile _profile;
        private string _securedToken;

        public IqHttpClient(string username, string password)
        {
            LoginModel = new LoginModel {Email = username, Password = password};
            HttpClient = new RestClient(new Uri("https://iqoption.com/api/"));
            AuthHttpClient = new RestClient(new Uri("https://auth.iqoption.com/api/v1.0/"));
        }

        internal IRestClient HttpClient { get; set; }
        internal IRestClient AuthHttpClient { get; set; }


        public string SecuredToken {
            get => _securedToken;
            private set => this.RaiseAndSetIfChanged(ref _securedToken, value);
        }

        public LoginModel LoginModel { get; }

        public Profile Profile {
            get => _profile;
            private set => this.RaiseAndSetIfChanged(ref _profile, value);
        }


        public void Dispose() { }

        #region Web-Methods

        public async Task<IqHttpResult<SsidResultMessage>> LoginAsync() {
            var request = new RestRequest("login", Method.POST) {RequestFormat = DataFormat.Json}
                .AddHeader("Content-Type", "application/x-www-form-urlencoded")
                .AddHeader("content-type", "multipart/form-data")
                .AddHeader("Accept", "application/json")
                .AddParameter("email", LoginModel.Email, ParameterType.QueryString)
                .AddParameter("password", LoginModel.Password, ParameterType.QueryString);

            var httpResult = await AuthHttpClient.ExecuteTaskAsync(request);

            switch (httpResult.StatusCode) {
                case HttpStatusCode.OK: {
                    var result = httpResult.Content.JsonAs<IqHttpResult<SsidResultMessage>>();
                    result.IsSuccessful = true;
                    SecuredToken = result.Data.Ssid;

                    _logger.Debug(L("Connected", result.Data.Ssid));

                    HttpClient.CookieContainer = new CookieContainer();
                    HttpClient.CookieContainer?.Add(new Cookie("ssid", SecuredToken, "/", "iqoption.com"));

                    // update profile
                    await GetProfileAsync();

                    return result;
                }
                default: {
                    var error = httpResult.Content.JsonAs<IqHttpResult<SsidResultMessage>>();
                    error.IsSuccessful = false;

                    return error;
                }
            }
        }


        public async Task<Profile> GetProfileAsync() {
            // send command
            var result = await ExecuteHttpClientAsync(new GetProfileCommand());

            //
            if (result != null && result.StatusCode == HttpStatusCode.OK) {
                var data = result.Content.JsonAs<IqHttpResult<Profile>>().GetContent();

                // log
                _logger.Trace(L("GetProfile",
                    $"Client Profile Updated UserId :{data.UserId}, trading with BalanceId: {data.BalanceId}"));

                // updated profile
                Profile = data;

                return data;
            }

            return null;
        }

        /// <inheritdoc cref="IIqHttpClient.ChangeBalanceAsync"/>
        public async Task<IqHttpResult<IHttpResultMessage>> ChangeBalanceAsync(long balanceId) {

            try {

                // send command
                var result = await ExecuteHttpClientAsync(new ChangeBalanceCommand(balanceId));

                if (result.IsSuccessful)
                    _logger.Info(R("changebalance", "Success"));
                else
                    _logger.Warn(R("changebalance", result.ErrorMessage));


                return result.Content.JsonAs<IqHttpResult<IHttpResultMessage>>();

            }
            catch {
                return null;
            }

        }

        public async Task<BalanceType> GetBalanceModeAsync()
        {
            var profile = await GetProfileAsync();

            return profile.BalanceType;
        }

        private Task<IRestResponse> ExecuteHttpClientAsync(IqOptionCommand cmd) {
            
            // send command
            var result = HttpClient.ExecuteTaskAsync(cmd);

            // response
            return result;
        }

        private string prefix() => (LoginModel?.Email ?? "CLIENT").PadRight(13).Substring(0, 13) + " |";
        private string L(string topic, string msg) => $"{prefix()} {topic.PadLeft(13).Substring(0, 13)} > {msg}";
        private string R(string topic, string msg) => $"{prefix()} {topic.PadLeft(13).Substring(0, 13)} < {msg}";

        #endregion
    }
}
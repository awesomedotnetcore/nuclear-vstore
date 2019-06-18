using System;
using System.Net.Http;
using System.Threading.Tasks;
using IdentityModel.Client;
using Microsoft.Extensions.Logging;

namespace CloningTool.Auth
{
    public class AimAuthService : IAuthService
    {
        private readonly AimOptions _options;
        private readonly ILogger<AimAuthService> _logger;
        private static readonly HttpClient HttpClient = new HttpClient();

        public AimAuthService(AimOptions options, ILogger<AimAuthService> logger)
        {
            _options = options;
            _logger = logger;
        }

        public async Task<(string, string)> AuthenticateAsync()
        {
            if (!string.IsNullOrEmpty(_options.AccessToken))
            {
                _logger.LogInformation("Using custom access token");
                return ("Bearer", _options.AccessToken);
            }

            var tokenRequest = PrepareParams();
            _logger.LogInformation("Requesting token from '{url}'...", tokenRequest.Address);
            var response = await HttpClient.RequestTokenAsync(tokenRequest);
            if (response.IsError)
            {
                throw new HttpRequestException($"Got an error of type {response.ErrorType} during authentication: {response.Error}; description: {response.ErrorDescription}");
            }

            return (response.TokenType, response.AccessToken);
        }

        private TokenRequest PrepareParams()
        {
            if (string.IsNullOrEmpty(_options.AccessTokenParams))
            {
                var result = string.IsNullOrEmpty(_options.AccessTokenUsername)
                                 ? new TokenRequest()
                                 : new PasswordTokenRequest
                                     {
                                         UserName = _options.AccessTokenUsername,
                                         Password = _options.AccessTokenPassword
                                     };

                result.Address = _options.AccessTokenUri;
                result.GrantType = _options.AccessTokenGrantType;
                result.ClientId = _options.AccessTokenClientId;
                result.ClientSecret = _options.AccessTokenClientSecret;
                result.Parameters["scope"] = _options.AccessTokenScope;

                return result;
            }
            else
            {
                var result = new TokenRequest { Address = _options.AccessTokenUri };
                foreach (var parameter in _options.AccessTokenParams.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pair = parameter.Split('=');
                    if (pair.Length != 2)
                    {
                        _logger.LogError("Invalid parameter pair: {param}", parameter);
                    }

                    result.Parameters[pair[0]] = pair[1];
                }

                return result;
            }
        }
    }
}
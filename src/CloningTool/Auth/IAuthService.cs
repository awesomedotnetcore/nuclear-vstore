using System.Threading.Tasks;

namespace CloningTool.Auth
{
    public interface IAuthService
    {
        Task<(string tokenType, string token)> AuthenticateAsync();
    }
}
using Microsoft.AspNetCore.Http;

namespace PataoSmartQueuing.Helpers
{
    public static class AdminAuth
    {
        public static bool IsLoggedIn(HttpContext context)
        {
            return context.Session.GetInt32("AdminID") != null;
        }

        public static string GetRole(HttpContext context)
        {
            return context.Session.GetString("AdminRole") ?? "";
        }

        public static bool HasRole(HttpContext context, params string[] roles)
        {
            var role = GetRole(context);
            return roles.Contains(role);
        }
    }
}
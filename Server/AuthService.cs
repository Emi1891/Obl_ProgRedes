using System;
using System.Linq;
using Grpc.Core;
using Dominio;

namespace Server
{
    public class AuthServiceImpl : AuthService.AuthServiceBase
    {
        public override Task<ValidateResponse> ValidateCredentials(ValidateRequest request, ServerCallContext context)
        {
            bool valid = false;
            lock (Server.GetUsersLock())
            {
                var users = Server.GetUsers();
                valid = users.Any(u => string.Equals(u.nombre, request.Username, StringComparison.Ordinal) && u.contrasena == request.Password);
            }

            return Task.FromResult(new ValidateResponse
            {
                Valid = valid,
                Message = valid ? "Credentials valid" : "Invalid credentials"
            });
        }

        public override Task<ClassLinkResponse> ValidateClassLink(ClassLinkRequest request, ServerCallContext context)
        {
            bool valid = false;
            string className = "";

            lock (Server.GetClassesLock())
            {
                var classes = Server.GetClasses();
                var clase = classes.FirstOrDefault(c => c.Link == request.Link);
                if (clase != null)
                {
                    valid = true;
                    className = clase.Nombre;
                }
            }

            return Task.FromResult(new ClassLinkResponse
            {
                Valid = valid,
                ClassName = className,
                Message = valid ? "Link valid" : "Invalid link"
            });
        }
    }
}


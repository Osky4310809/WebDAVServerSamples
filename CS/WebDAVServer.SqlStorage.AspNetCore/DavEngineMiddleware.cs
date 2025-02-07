using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;

using ITHit.Server;
using ITHit.WebDAV.Server;
using ITHit.WebDAV.Server.Core;

using WebDAVServer.SqlStorage.AspNetCore.Options;

namespace WebDAVServer.SqlStorage.AspNetCore
{
    /// <summary>
    /// An ASP.NET Core middleware that processes WebDAV requests.
    /// </summary>
    public class DavEngineMiddleware
    {
        /// <summary>
        /// WebDAV Engine instance.
        /// </summary>
        private readonly DavEngineCore engine;

        /// <summary>
        /// Initializes new instance of this class based on the WebDAV Engine instance.
        /// </summary>     
        /// <param name="next">Next middleware instance.</param>
        /// <param name="davEngineCore">WebDAV Engine instance.</param>
        public DavEngineMiddleware(RequestDelegate next, DavEngineCore engineCore)
        {
            this.engine = engineCore;
        }

        /// <summary>
        /// Processes WebDAV request.
        /// </summary>
        public async Task Invoke(HttpContext context, ContextBaseAsync davContext, IOptions<DavContextOptions> tmp, ILogger logger)
        {
            await engine.RunAsync(davContext);
        }
    }

    /// <summary>
    /// Extension methods to add WebDAV Engine capabilities to an HTTP application pipeline.
    /// </summary>
    public static class DavEngineMiddlewareExtensions
    {
        private static IConfiguration Configuration;

        /// <summary>
        /// Adds a WebDAV services to the specified <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> instance.</param>
        /// <param name="configuration">The <see cref="IConfigurationRoot"/> instance.</param>
        /// <param name="env">The <see cref="IHostingEnvironment"/> instance.</param>
        public static void AddWebDav(this IServiceCollection services, IConfigurationRoot configuration, IHostingEnvironment env)
        {
            Configuration = configuration;
            services.AddSingleton<DavEngineCore>();
            services.AddSingleton<ILogger, DavLoggerCore>();

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddScoped<ContextBaseAsync, DavContext>();
            services.Configure<DavEngineOptions>(async options => await Configuration.GetSection("EngineOptions").ReadOptionsAsync(options));
            services.Configure<DavContextOptions>(async options => await Configuration.GetSection("ContextOptions").ReadOptionsAsync(options, env));
            services.Configure<DavLoggerOptions>(async options => await Configuration.GetSection("LoggerOptions").ReadOptionsAsync(options, env));
            services.Configure<DavUsersOptions>(options => Configuration.GetSection("Users").Bind(options));
        }

        /// <summary>
        /// Adds a WebDAV Engine middleware type to the application's request pipeline.
        /// </summary>
        /// <param name="builder">The <see cref="IApplicationBuilder"/> instance.</param>
        /// <returns>The <see cref="IApplicationBuilder"/> instance. </returns>
        public static IApplicationBuilder UseWebDav(this IApplicationBuilder builder, IHostingEnvironment env)
        {
            CreateDatabaseSchema(builder, env);
            return builder.UseMiddleware<DavEngineMiddleware>();
        }
        /// <summary>
        /// Creates database if it does not exist.
        /// </summary>
        /// <param name="builder">The <see cref="IApplicationBuilder"/> instance.</param>
        /// <param name="env">The <see cref="IApplicationBuilder"/> instance.</param>
        private static void CreateDatabaseSchema(IApplicationBuilder builder, IHostingEnvironment env)
        {
            bool databaseExists = false;
            DavContextOptions contextOptions = builder.ApplicationServices.GetService<IOptions<DavContextOptions>>().Value;
            SqlConnectionStringBuilder sqlConnectionStringBuilder = new SqlConnectionStringBuilder(contextOptions.ConnectionString);
            // extracts initial catalog name
            string databaseName = sqlConnectionStringBuilder.InitialCatalog;
            // sets initial catalog to master 
            sqlConnectionStringBuilder.InitialCatalog = "master";

            using (SqlConnection sqlConnection = new SqlConnection(sqlConnectionStringBuilder.ConnectionString))
            {
                sqlConnection.Open();
                using (SqlCommand sqlCommand = new SqlCommand($"SELECT db_id('{databaseName}')", sqlConnection))
                {
                    databaseExists = (sqlCommand.ExecuteScalar() != DBNull.Value);
                }

                if (!databaseExists)
                {
                    RunScript(sqlConnection, File.ReadAllText(env.ContentRootPath + "\\DB.sql"));
                }
            }
        }

        /// <summary>
        /// Executes sql script.
        /// </summary>
        /// <param name="connection">The <see cref="SqlConnection"/> instance.</param>
        /// <param name="sqlScript">sql script</param>
        private static void RunScript(SqlConnection connection, string sqlScript)
        {
            string[] commands = sqlScript.Split(new[] { "GO" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string command in commands)
            {
                if (!string.IsNullOrWhiteSpace(command))
                {
                    using (SqlCommand sqlCommand = new SqlCommand(command, connection))
                    {
                        sqlCommand.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}


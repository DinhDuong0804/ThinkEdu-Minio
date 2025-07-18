using Microsoft.OpenApi.Models;
using Serilog;
using System.Reflection;
using ThinkEdu_Minio.Configurations;
using ThinkEdu_Minio.Services.Implements;
using ThinkEdu_Minio.Services.Interfaces;

namespace ThinkEdu_Minio
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Cấu hình Serilog 
            var applicationName = builder.Environment.ApplicationName?.ToLower().Replace(".", "-");
            var environmentName = builder.Environment.EnvironmentName ?? "Development";

            builder.Host.UseSerilog((context, configuration) =>
                configuration
                    .WriteTo.Debug()
                    .WriteTo.Console(outputTemplate:
                        "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}")
                    .WriteTo.File($"Logs/{applicationName}-.txt", rollingInterval: RollingInterval.Day)
                    .Enrich.FromLogContext()
                    .Enrich.WithMachineName()
                    .Enrich.WithProperty("Environment", environmentName)
                    .Enrich.WithProperty("Application", applicationName)
                    .ReadFrom.Configuration(context.Configuration));

            // Add CORS policy
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });

            // cấu hình Swagger
            builder.Services.AddSwaggerGen(options =>
            {
                options.EnableAnnotations();
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "ThinkEdu Minio Service API",
                    Version = "v1",
                    Description = "API cho phép quản lý và lưu trữ file với Minio",

                });

                // Đọc XML Comments nếu có
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                {
                    options.IncludeXmlComments(xmlPath);
                }
            });

            builder.Services.AddControllers();
            builder.Services.AddOpenApi();

            // Loại bỏ giới hạn kích thước file upload
            builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
            {
                // Đặt giá trị cực lớn (thực tế là vô hạn)
                options.MultipartBodyLengthLimit = long.MaxValue;
                options.ValueLengthLimit = int.MaxValue;
                options.MultipartHeadersLengthLimit = int.MaxValue;
            });

            // Cấu hình Kestrel không giới hạn kích thước request 
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = null; // null = không giới hạn
            });

            builder.Services.AddScoped<IFileService, FileServices>();

            builder.Services.Configure<MinioSettings>(builder.Configuration.GetSection("Minio"));

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MinIO API v1");
                    c.RoutePrefix = "swagger";
                    c.DocumentTitle = "ThinkEdu MinIO API";
                    c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
                });
            }

            app.UseCors("AllowAll");

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
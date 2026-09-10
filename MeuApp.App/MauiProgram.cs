using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MeuApp.Shared.Data;
using MeuApp.Shared.Services;

namespace MeuApp.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		// Configuração do banco SQLite no diretório de dados do aplicativo
		var dbPath = Path.Combine(FileSystem.AppDataDirectory, "app_validade.db");
		builder.Services.AddDbContextFactory<AppDbContext>(options =>
		{
			options.UseSqlite($"Data Source={dbPath}");
		});

		builder.Services.AddScoped<IValidadeService, ValidadeService>();
		builder.Services.AddSingleton<IEmailService, EmailService>();
		builder.Services.AddSingleton<IAuthService, AuthService>();

		var app = builder.Build();

		// Inicialização e Carga inicial (Seed) do banco
		Task.Run(async () =>
		{
			try
			{
				using var scope = app.Services.CreateScope();
				var service = scope.ServiceProvider.GetRequiredService<IValidadeService>();
				await service.InicializarBancoESeedAsync();
			}
			catch
			{
				// Log / tratamento defensivo de inicialização
			}
		});

		return app;
	}
}

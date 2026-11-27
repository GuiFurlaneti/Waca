using Waca.Api.Autenticacao;

var builder = WebApplication.CreateBuilder(args);

builder.AdicionarAutenticacaoWaca();

var app = builder.Build();

app.UsarAutenticacaoWaca();

app.Run();

/// <summary>Ponto de entrada da API (público para os testes HTTP com WebApplicationFactory).</summary>
public partial class Program;

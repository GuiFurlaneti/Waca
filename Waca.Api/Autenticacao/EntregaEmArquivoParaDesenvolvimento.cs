using System.Text;
using Waca.Infrastructure.Identidade.Desafios;

namespace Waca.Api.Autenticacao;

/// <summary>
/// Entrega de códigos <b>somente em Development</b>: grava cada mensagem como arquivo <c>.eml</c> em um diretório
/// privado fora do repositório, para testes manuais sem provedor de e-mail.
/// </summary>
/// <remarks>
/// Não escreve no console nem em log, não expõe nada por HTTP ou OpenAPI e recusa ser construída fora de
/// Development. O diretório padrão fica no perfil local do usuário (<c>LocalApplicationData/Waca/emails-desenvolvimento</c>),
/// portanto fora do repositório; em Linux e macOS é criado com permissão apenas para o dono.
/// </remarks>
public sealed class EntregaEmArquivoParaDesenvolvimento : IEntregaDeCodigoDeAcesso
{
    private readonly string _diretorio;

    /// <exception cref="InvalidOperationException">Ambiente diferente de Development.</exception>
    public EntregaEmArquivoParaDesenvolvimento(IHostEnvironment ambiente, string diretorio)
    {
        ArgumentNullException.ThrowIfNull(ambiente);
        if (!ambiente.IsDevelopment())
        {
            throw new InvalidOperationException("A entrega de códigos em arquivo só pode ser usada no ambiente Development.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(diretorio);
        _diretorio = Path.GetFullPath(diretorio);
    }

    public static string DiretorioPadrao =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Waca", "emails-desenvolvimento");

    public async Task EntregarAsync(CodigoParaEntrega entrega, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entrega);

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(_diretorio);
        }
        else
        {
            Directory.CreateDirectory(_diretorio, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var conteudo = new StringBuilder()
            .Append("To: ").Append(entrega.EmailDestino).Append("\r\n")
            .Append("Subject: Seu código de acesso ao Waca\r\n")
            .Append("Content-Type: text/plain; charset=utf-8\r\n")
            .Append("\r\n")
            .Append("Seu código de acesso é ").Append(entrega.Codigo).Append(".\r\n")
            .Append("Ele vale até ").Append(entrega.ExpiraEm.ToString("u", System.Globalization.CultureInfo.InvariantCulture)).Append(".\r\n")
            .ToString();

        var arquivo = Path.Combine(_diretorio, $"{entrega.DesafioId:N}.eml");
        await using var fluxo = new FileStream(arquivo, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using var escritor = new StreamWriter(fluxo, new UTF8Encoding(false));
        await escritor.WriteAsync(conteudo.AsMemory(), cancellationToken);
    }
}

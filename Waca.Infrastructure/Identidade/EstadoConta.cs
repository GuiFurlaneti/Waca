namespace Waca.Infrastructure.Identidade;

/// <summary>
/// Estado administrativo da conta, gravado como texto na coluna <c>Estado</c> de <c>identidade.AspNetUsers</c>.
/// </summary>
/// <remarks>
/// <para>
/// É independente do bloqueio temporário do Identity (<c>LockoutEnd</c>/<c>LockoutEnabled</c>): uma conta
/// desativada continua desativada mesmo que o bloqueio temporário seja removido ou nunca tenha existido.
/// </para>
/// <para>
/// Não existe valor padrão válido: <c>default(EstadoConta)</c> (0) não é um estado, e o banco recusa qualquer
/// texto fora de <c>Ativa</c> e <c>Desativada</c> pela restrição <c>CK_AspNetUsers_Estado</c>.
/// </para>
/// </remarks>
public enum EstadoConta
{
    /// <summary>A conta pode ser usada, desde que o e-mail esteja confirmado e não haja bloqueio temporário.</summary>
    Ativa = 1,

    /// <summary>Desativação administrativa. A conta não pode ser usada para acesso.</summary>
    Desativada = 2,
}

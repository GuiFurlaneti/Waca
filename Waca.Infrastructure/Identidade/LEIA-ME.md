# Identidade persistida (MVP-04A)

Modelo de contas do ASP.NET Core Identity e tabela de chaves do Data Protection, no schema `identidade`
do PostgreSQL. **Não há login, cadastro, cookies, sessões, endpoints nem envio de e-mail nesta entrega.**

## Modelo

- `UsuarioWaca : IdentityUser<Guid>`. O `Id` é o `PessoaId` da Application: é fornecido por quem cria a conta
  (Guid v7 previsto), nunca é gerado pelo banco e nunca muda. O nome de usuário do Identity deriva do `Id`.
- `Estado` (`Ativa` | `Desativada`) é o estado administrativo, separado do bloqueio temporário do Identity
  (`LockoutEnd`). Não tem valor padrão válido; o banco recusa qualquer outro texto.
- `PermiteAcesso()` exige conta ativa **e** e-mail confirmado. O bloqueio temporário é verificado à parte.
- Nenhuma relação com vaquinhas ou participações.

## Garantias no banco

| Regra | Onde |
|---|---|
| E-mail normalizado único | Índice `EmailIndex` do Identity, tornado `UNIQUE` |
| E-mail e e-mail normalizado obrigatórios e não vazios | `NOT NULL` + `CK_AspNetUsers_Email_NaoVazio` |
| Estado válido | `NOT NULL` + `CK_AspNetUsers_Estado` |
| Identificador não vazio | `CK_AspNetUsers_Id_NaoVazio` |

`RequireUniqueEmail` (em `OpcoesDaIdentidade`) é só a validação de aplicação: não resiste a cadastros
simultâneos. A garantia é o índice.

## Configuração única

- `OpcoesDaIdentidade.Configurar` fixa a versão 2 do schema do Identity (sem papéis e sem passkeys),
  `MaxLengthForKeys = 128`, e-mail único e e-mail confirmado. O contexto recusa construir o modelo com
  outras opções.
- `WacaIdentidadeDbContext.UsarPostgres` configura o provedor com o histórico próprio
  `identidade.__EFMigrationsHistory`. O contexto recusa outra configuração de histórico.
- A futura API deve registrar o contexto com `UsarPostgres` e `AddIdentityCore<UsuarioWaca>(OpcoesDaIdentidade.Configurar)`.

## Migrations

Geradas pela fábrica de design-time, que não contém connection string:

```
dotnet ef migrations add <Nome> --project Waca.Infrastructure --startup-project Waca.Infrastructure --context WacaIdentidadeDbContext --output-dir Identidade/Migrations
```

Nenhuma migration é aplicada automaticamente. As migrations de vaquinhas e seu histórico (`public`) não
são afetados.

## Data Protection

A tabela `DataProtectionKeys` só prepara a persistência das chaves; nada configura Data Protection agora.
**Exigência futura:** quando a API passar a usar Data Protection persistida no banco, a configuração de
produção deverá proteger as chaves em repouso por certificado ou mecanismo seguro equivalente, porque
gravar as chaves em banco desativa a criptografia em repouso padrão. Esta entrega não autoriza sessões
de produção.

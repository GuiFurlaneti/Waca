# Waca.Infrastructure.Tests

Testes de integração da persistência com **PostgreSQL real e descartável**, criado pelo Testcontainers.
Não há SQLite, EF Core InMemory nem conexão com qualquer outro servidor PostgreSQL. Nenhum banco de
produção é necessário ou usado.

## Pré-requisito

**Docker em execução na própria máquina**: socket Unix, named pipe do Windows ou TCP em loopback.
Docker remoto é recusado, porque a porta do PostgreSQL é publicada apenas no loopback do host do Docker,
que não equivale ao loopback da máquina que executa os testes.

Sem Docker, os testes que dependem de banco **falham** com `DockerUnavailableException`. Eles não são
ignorados nem dados como aprovados. Os testes de arquitetura e de segurança da pasta `Postgres/` não
dependem de Docker.

## Executar

```
dotnet test Waca.slnx --configuration Release
```

O mesmo comando roda no GitHub Actions (`.github/workflows/dotnet-ci.yml`), cujo runner Linux tem Docker.

## Como o servidor de testes é criado

- **Imagem fixada:** `postgres:16.13`, pelo digest em `Postgres/ServidorPostgres.cs`.
- **Credenciais:** senha aleatória gerada a cada execução; nenhuma credencial fica no repositório.
- **Rede:** porta do host escolhida dinamicamente pelo Docker e publicada só em `127.0.0.1`. A restrição é
  exigida na criação do contêiner e conferida depois de iniciado, pela inspeção das portas publicadas.
  Se não puder ser aplicada ou confirmada, os testes falham.
- **Conexão:** a connection string é obtida do contêiner já iniciado.
- **Descarte:** se a inicialização falhar depois de o contêiner ser criado, ele é descartado
  explicitamente. Ao final, o contêiner é removido; o Resource Reaper do Testcontainers (Ryuk) continua
  habilitado como proteção adicional.

## Bancos por teste

- Cada teste cria o próprio banco, com nome gerado internamente no formato `waca_teste_<32 hex>`. Nenhum
  nome vem de fora.
- Todo `CREATE DATABASE` e `DROP DATABASE` usa um nome validado contra esse padrão, o que exclui
  `postgres`, `template0`, `template1` e qualquer nome arbitrário.
- Só bancos cuja criação foi confirmada nesta execução ficam registrados, e só eles podem ser removidos.
  Não existe remoção por prefixo nem listagem de bancos do servidor.
- Se a aplicação das migrations falhar logo após a criação, o banco é removido e a falha original é
  mantida. Se a remoção também falhar, as duas falhas são relatadas juntas.

## Migrations

As migrations ficam em `Waca.Infrastructure/Persistencia/Migrations` e são geradas com `dotnet-ef` na mesma
versão do EF Core (10.0.15), sem instalação global:

```
dotnet tool install dotnet-ef --version 10.0.15 --tool-path <pasta-temporaria>
<pasta-temporaria>/dotnet-ef migrations add <Nome> --project Waca.Infrastructure/Waca.Infrastructure.csproj --output-dir Persistencia/Migrations
```

Gerar uma migration não acessa banco. As migrations não são aplicadas automaticamente na inicialização de
nenhuma aplicação.

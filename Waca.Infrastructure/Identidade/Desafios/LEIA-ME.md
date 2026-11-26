# Desafios de acesso (MVP-04B)

Motor interno de códigos temporários, persistidos e consumidos atomicamente no PostgreSQL
(`identidade.desafios_acesso`). **Não há login HTTP, cookies, sessões, criação de contas nem envio real de e-mail.**

## Parâmetros provisórios (`OpcoesDeDesafios`)

| Parâmetro | Padrão |
|---|---|
| Dígitos do código | 8 |
| Validade | 10 minutos |
| Tentativas por desafio | 5 |
| Intervalo mínimo entre emissões (mesmo e-mail e finalidade) | 60 segundos |
| Cota curta | 3 emissões em 15 minutos |
| Cota longa | 10 emissões em 24 horas |
| Retenção após a expiração | 7 dias |

São valores iniciais testáveis, não decisão de UX ou de produção. Os componentes validam as opções na
construção.

## Código e chave

- Código gerado por `RandomNumberGenerator`, com zeros à esquerda preservados.
- Gravado somente como HMAC-SHA256 (`segredo_hash`, 32 bytes), com chave secreta fora do banco. A mensagem
  autenticada liga, com prefixo de tamanho, o id do desafio, a finalidade, o e-mail normalizado e o código.
- `versao_chave` permite rotação: o chaveiro aceita versões antigas para verificar e emite com a atual.
  Versão desconhecida é recusada.
- Não existe chave padrão nem de desenvolvimento. Sem configuração válida (Base64, 32 a 64 bytes, não trivial,
  sem repetição entre versões), o chaveiro não é criado e os serviços não funcionam.

## Emissão

1. Valida o e-mail (`EmailAddressAttribute`, sem espaços nas pontas, até 256) e normaliza com o `ILookupNormalizer`.
2. Abre transação e obtém `pg_advisory_xact_lock` para (finalidade, e-mail normalizado): emissões concorrentes, em
   qualquer instância, são serializadas.
3. Confere as cotas pelas próprias linhas da tabela.
4. Invalida o desafio aberto anterior, grava o novo (só o HMAC) e confirma.
5. Somente depois do commit chama `IEntregaDeCodigoDeAcesso`.

**Falha na entrega:** o desafio é invalidado em nova transação, a emissão continua contando para a cota e o
resultado é `EntregaFalhou`. Não há fila nem cópia do código. `Emitido` significa apenas que o mecanismo de
entrega aceitou a mensagem, não que ela chegou.

## Verificação e consumo

- `SELECT ... FOR UPDATE` na linha do desafio, dentro da transação (`READ COMMITTED`).
- Recusa: inexistente, consumido, invalidado, expirado (mesmo antes da limpeza), tentativas esgotadas, versão
  de chave indisponível ou código que não confere (o HMAC inclui finalidade e destinatário).
- Código incorreto incrementa `tentativas`; ao atingir o máximo o desafio é invalidado.
- Sucesso: `UPDATE` condicional que precisa afetar exatamente uma linha.
- `VerificarEConsumirAsync` usa a transação do chamador e não confirma nada: o consumo pode ser desfeito se a
  operação maior (criação de conta, sessão — MVP-04C) falhar. Em recusa, o chamador deve **confirmar** a
  transação para que a tentativa conte. `VerificarEConsumirEmTransacaoPropriaAsync` faz isso sozinho.
- Tentativas erradas nunca acionam o lockout da conta (`AccessFailedAsync`).

## Limpeza e retenção (provisória)

`LimpezaDeDesafios.RemoverAntigosAsync` apaga apenas desafios expirados há mais que a retenção (7 dias). Como a
retenção cobre a janela longa, nenhuma emissão que ainda conta para a cota é apagada. A operação é idempotente e
não reabre nada: um desafio apagado é recusado. O agendamento fica para a fase de hospedagem.

## Riscos residuais

- **Esgotamento de cota por terceiros:** quem conhece um e-mail pode consumir a cota dele e atrasar o acesso do
  titular por até 24 horas. A limitação por IP (MVP-04C, depende do proxy) reduz esse risco.
- **Relógios:** os horários vêm do `TimeProvider` da aplicação; instâncias precisam de relógio sincronizado (NTP).
- **Falha ao invalidar após falha de entrega:** fica registrada em log; o código não foi entregue nem gravado e o
  desafio expira na validade configurada.
- **Finalidade `TrocaDeEmail`:** existe só para separar códigos por finalidade; nenhum fluxo a usa ainda e
  `pessoa_id` não tem chave estrangeira até que esse fluxo seja aprovado.

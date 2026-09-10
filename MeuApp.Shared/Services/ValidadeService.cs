using System.Text;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MiniExcelLibs;
using MeuApp.Shared.Data;
using MeuApp.Shared.Models;

namespace MeuApp.Shared.Services;

public class ValidadeService : IValidadeService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public ValidadeService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task InicializarBancoESeedAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Migração defensiva para bancos SQLite existentes
        try
        {
            var conn = context.Database.GetDbConnection();
            await conn.OpenAsync();

            using var cmdCheck = conn.CreateCommand();
            cmdCheck.CommandText = "PRAGMA table_info(RegistrosValidade);";
            using var reader = await cmdCheck.ExecuteReaderAsync();
            var temLojaId = false;
            var temTabela = false;
            while (await reader.ReadAsync())
            {
                temTabela = true;
                var col = reader.GetString(1);
                if (col.Equals("LojaId", StringComparison.OrdinalIgnoreCase))
                {
                    temLojaId = true;
                    break;
                }
            }
            await reader.CloseAsync();

            if (temTabela && !temLojaId)
            {
                using var cmdAlter = conn.CreateCommand();
                cmdAlter.CommandText = @"
                    ALTER TABLE RegistrosValidade ADD COLUMN LojaId INTEGER NOT NULL DEFAULT 1;
                    UPDATE RegistrosValidade SET LojaId = (SELECT LojaId FROM Produtos WHERE Produtos.Id = RegistrosValidade.ProdutoId) WHERE EXISTS (SELECT 1 FROM Produtos WHERE Produtos.Id = RegistrosValidade.ProdutoId);
                ";
                await cmdAlter.ExecuteNonQueryAsync();
            }

            // 2. Migra Produtos para remover LojaId e FK legada caso a tabela antiga ainda possua essa coluna
            using var cmdCheckProd = conn.CreateCommand();
            cmdCheckProd.CommandText = "PRAGMA table_info(Produtos);";
            using var readerProd = await cmdCheckProd.ExecuteReaderAsync();
            var prodTemLojaId = false;
            var temTabelaProd = false;
            while (await readerProd.ReadAsync())
            {
                temTabelaProd = true;
                var col = readerProd.GetString(1);
                if (col.Equals("LojaId", StringComparison.OrdinalIgnoreCase))
                {
                    prodTemLojaId = true;
                    break;
                }
            }
            await readerProd.CloseAsync();

            if (temTabelaProd && prodTemLojaId)
            {
                using var cmdMigrateProd = conn.CreateCommand();
                cmdMigrateProd.CommandText = @"
                    PRAGMA foreign_keys=OFF;

                    CREATE TABLE ""Produtos_new"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Produtos"" PRIMARY KEY AUTOINCREMENT,
                        ""CodigoBarras"" TEXT NOT NULL,
                        ""Nome"" TEXT NOT NULL
                    );

                    INSERT INTO ""Produtos_new"" (""Id"", ""CodigoBarras"", ""Nome"")
                    SELECT ""Id"", ""CodigoBarras"", ""Nome"" FROM ""Produtos"";

                    DROP TABLE ""Produtos"";

                    ALTER TABLE ""Produtos_new"" RENAME TO ""Produtos"";

                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Produtos_CodigoBarras"" ON ""Produtos"" (""CodigoBarras"");

                    PRAGMA foreign_keys=ON;
                ";
                await cmdMigrateProd.ExecuteNonQueryAsync();
            }
        }
        catch
        {
        }

        await context.Database.EnsureCreatedAsync();

        // Deduplica produtos caso existam produtos com mesmo CodigoBarras para lojas diferentes
        try
        {
            var produtos = await context.Produtos.ToListAsync();
            var grupos = produtos.GroupBy(p => p.CodigoBarras).Where(g => g.Count() > 1).ToList();
            foreach (var g in grupos)
            {
                var principal = g.First();
                var repetidos = g.Skip(1).ToList();
                foreach (var rep in repetidos)
                {
                    var validades = await context.RegistrosValidade.Where(r => r.ProdutoId == rep.Id).ToListAsync();
                    foreach (var v in validades)
                    {
                        v.ProdutoId = principal.Id;
                    }
                    context.Produtos.Remove(rep);
                }
            }
            await context.SaveChangesAsync();
        }
        catch
        {
        }

        // Carga inicial (Seed) caso o banco esteja vazio
        if (!await context.Lojas.AnyAsync())
        {
            var loja1 = new Loja { Nome = "Loja 01 - Centro", CodigoLoja = "LJ-01" };
            var loja2 = new Loja { Nome = "Loja 02 - Zona Sul", CodigoLoja = "LJ-02" };
            context.Lojas.AddRange(loja1, loja2);
            await context.SaveChangesAsync();

            var hoje = DateTime.Today;

            var p1 = new Produto { CodigoBarras = "7891000100101", Nome = "Iogurte Natural 170g" };
            var p2 = new Produto { CodigoBarras = "7891000200202", Nome = "Queijo Mussarela Fatiado 500g" };
            var p3 = new Produto { CodigoBarras = "7891000300303", Nome = "Pão de Forma Tradicional 400g" };
            var p4 = new Produto { CodigoBarras = "7891000400404", Nome = "Presunto Cozido 200g" };
            var p5 = new Produto { CodigoBarras = "7891000500505", Nome = "Leite Longa Vida 1L" };

            context.Produtos.AddRange(p1, p2, p3, p4, p5);
            await context.SaveChangesAsync();

            var validades = new List<RegistroValidade>
            {
                new() { ProdutoId = p4.Id, LojaId = loja1.Id, DataColeta = hoje, DataValidade = hoje.AddDays(-3), EmPromocao = false, Status = "Ativo" },
                new() { ProdutoId = p1.Id, LojaId = loja1.Id, DataColeta = hoje, DataValidade = hoje.AddDays(4), EmPromocao = true, Status = "Ativo" },
                new() { ProdutoId = p2.Id, LojaId = loja1.Id, DataColeta = hoje, DataValidade = hoje.AddDays(9), EmPromocao = false, Status = "Ativo" },
                new() { ProdutoId = p3.Id, LojaId = loja1.Id, DataColeta = hoje, DataValidade = hoje.AddDays(14), EmPromocao = false, Status = "Ativo" },
                new() { ProdutoId = p5.Id, LojaId = loja2.Id, DataColeta = hoje, DataValidade = hoje.AddDays(45), EmPromocao = false, Status = "Ativo" },
                new() { ProdutoId = p2.Id, LojaId = loja1.Id, DataColeta = hoje.AddDays(-7), DataValidade = hoje.AddDays(-5), EmPromocao = true, Status = "Baixado", DataBaixa = DateTime.Now.AddDays(-1), MotivoBaixa = "Descarte/Vencido" }
            };

            context.RegistrosValidade.AddRange(validades);
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<Loja>> GetLojasAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Lojas.OrderBy(l => l.Nome).ToListAsync();
    }

    public async Task<Loja?> GetLojaPorIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Lojas.FindAsync(id);
    }

    public async Task<Loja> CriarLojaAsync(Loja loja)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Lojas.Add(loja);
        await context.SaveChangesAsync();
        return loja;
    }

    public async Task<Loja?> AtualizarLojaAsync(int id, string nome, string codigoLoja)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var loja = await context.Lojas.FindAsync(id);
        if (loja != null)
        {
            loja.Nome = nome.Trim();
            loja.CodigoLoja = codigoLoja.Trim().ToUpperInvariant();
            await context.SaveChangesAsync();
        }
        return loja;
    }

    public async Task ExcluirLojaAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var loja = await context.Lojas.FindAsync(id);
        if (loja != null)
        {
            context.Lojas.Remove(loja);
            await context.SaveChangesAsync();
        }
    }

    public async Task<Produto?> BuscarProdutoPorCodigoBarrasAsync(string codigoBarras)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var codigoTratado = codigoBarras.Trim();

        return await context.Produtos
            .Include(p => p.Validades.Where(v => v.Status == "Ativo"))
                .ThenInclude(v => v.Loja)
            .FirstOrDefaultAsync(p => p.CodigoBarras == codigoTratado);
    }

    public async Task<List<RegistroValidade>> GetValidadesAtivasDoProdutoNaLojaAsync(int produtoId, int lojaId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.RegistrosValidade
            .Include(r => r.Loja)
            .Include(r => r.Produto)
            .Where(r => r.ProdutoId == produtoId && r.LojaId == lojaId && r.Status == "Ativo")
            .OrderBy(r => r.DataValidade)
            .ToListAsync();
    }

    public async Task<Produto> CadastrarProdutoComValidadeAsync(int lojaId, string codigoBarras, string nome, DateTime dataValidade, bool emPromocao)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var codigoTratado = codigoBarras.Trim();

        var produto = await context.Produtos.FirstOrDefaultAsync(p => p.CodigoBarras == codigoTratado);

        if (produto == null)
        {
            produto = new Produto
            {
                CodigoBarras = codigoTratado,
                Nome = nome.Trim()
            };
            context.Produtos.Add(produto);
            await context.SaveChangesAsync();
        }
        else if (!string.IsNullOrWhiteSpace(nome) && produto.Nome != nome.Trim())
        {
            produto.Nome = nome.Trim();
            await context.SaveChangesAsync();
        }

        var registro = new RegistroValidade
        {
            ProdutoId = produto.Id,
            LojaId = lojaId,
            DataColeta = DateTime.Today,
            DataValidade = dataValidade.Date,
            EmPromocao = emPromocao,
            Status = "Ativo"
        };

        context.RegistrosValidade.Add(registro);
        await context.SaveChangesAsync();

        return (await BuscarProdutoPorCodigoBarrasAsync(codigoTratado))!;
    }

    public async Task<Produto> CadastrarProdutoComValidadeMultiplasLojasAsync(IEnumerable<int> lojasIds, string codigoBarras, string nome, DateTime dataValidade, bool emPromocao)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var codigoTratado = codigoBarras.Trim();

        var produto = await context.Produtos.FirstOrDefaultAsync(p => p.CodigoBarras == codigoTratado);

        if (produto == null)
        {
            produto = new Produto
            {
                CodigoBarras = codigoTratado,
                Nome = nome.Trim()
            };
            context.Produtos.Add(produto);
            await context.SaveChangesAsync();
        }
        else if (!string.IsNullOrWhiteSpace(nome) && produto.Nome != nome.Trim())
        {
            produto.Nome = nome.Trim();
            await context.SaveChangesAsync();
        }

        foreach (var lojaId in lojasIds.Distinct())
        {
            var jaExiste = await context.RegistrosValidade.AnyAsync(r => 
                r.ProdutoId == produto.Id && 
                r.LojaId == lojaId && 
                r.DataValidade == dataValidade.Date && 
                r.Status == "Ativo");

            if (!jaExiste)
            {
                context.RegistrosValidade.Add(new RegistroValidade
                {
                    ProdutoId = produto.Id,
                    LojaId = lojaId,
                    DataColeta = DateTime.Today,
                    DataValidade = dataValidade.Date,
                    EmPromocao = emPromocao,
                    Status = "Ativo"
                });
            }
        }

        await context.SaveChangesAsync();
        return (await BuscarProdutoPorCodigoBarrasAsync(codigoTratado))!;
    }

    public async Task<RegistroValidade> AdicionarValidadeAoProdutoAsync(int produtoId, int lojaId, DateTime dataValidade, bool emPromocao)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var registro = new RegistroValidade
        {
            ProdutoId = produtoId,
            LojaId = lojaId,
            DataColeta = DateTime.Today,
            DataValidade = dataValidade.Date,
            EmPromocao = emPromocao,
            Status = "Ativo"
        };

        context.RegistrosValidade.Add(registro);
        await context.SaveChangesAsync();
        return registro;
    }

    public async Task<List<RegistroValidade>> AdicionarValidadeMultiplasLojasAsync(int produtoId, IEnumerable<int> lojasIds, DateTime dataValidade, bool emPromocao)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var novosRegistros = new List<RegistroValidade>();

        foreach (var lojaId in lojasIds.Distinct())
        {
            var jaExiste = await context.RegistrosValidade.AnyAsync(r =>
                r.ProdutoId == produtoId &&
                r.LojaId == lojaId &&
                r.DataValidade == dataValidade.Date &&
                r.Status == "Ativo");

            if (!jaExiste)
            {
                var reg = new RegistroValidade
                {
                    ProdutoId = produtoId,
                    LojaId = lojaId,
                    DataColeta = DateTime.Today,
                    DataValidade = dataValidade.Date,
                    EmPromocao = emPromocao,
                    Status = "Ativo"
                };
                context.RegistrosValidade.Add(reg);
                novosRegistros.Add(reg);
            }
        }

        await context.SaveChangesAsync();
        return novosRegistros;
    }

    public async Task<RegistroValidade?> CopiarValidadeAsync(int validadeId, int lojaDestinoId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var original = await context.RegistrosValidade.FindAsync(validadeId);
        if (original == null) return null;

        var jaExiste = await context.RegistrosValidade.AnyAsync(r =>
            r.ProdutoId == original.ProdutoId &&
            r.LojaId == lojaDestinoId &&
            r.DataValidade == original.DataValidade.Date &&
            r.Status == "Ativo");

        if (jaExiste) return null;

        var copia = new RegistroValidade
        {
            ProdutoId = original.ProdutoId,
            LojaId = lojaDestinoId,
            DataColeta = DateTime.Today,
            DataValidade = original.DataValidade.Date,
            EmPromocao = original.EmPromocao,
            Status = "Ativo"
        };

        context.RegistrosValidade.Add(copia);
        await context.SaveChangesAsync();
        return copia;
    }

    public async Task<int> CopiarValidadeParaTodasLojasAsync(int validadeId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var original = await context.RegistrosValidade.FindAsync(validadeId);
        if (original == null) return 0;

        var outrasLojas = await context.Lojas
            .Where(l => l.Id != original.LojaId)
            .ToListAsync();

        int copiasCriadas = 0;
        foreach (var loja in outrasLojas)
        {
            var jaExiste = await context.RegistrosValidade.AnyAsync(r =>
                r.ProdutoId == original.ProdutoId &&
                r.LojaId == loja.Id &&
                r.DataValidade == original.DataValidade.Date &&
                r.Status == "Ativo");

            if (!jaExiste)
            {
                context.RegistrosValidade.Add(new RegistroValidade
                {
                    ProdutoId = original.ProdutoId,
                    LojaId = loja.Id,
                    DataColeta = DateTime.Today,
                    DataValidade = original.DataValidade.Date,
                    EmPromocao = original.EmPromocao,
                    Status = "Ativo"
                });
                copiasCriadas++;
            }
        }

        if (copiasCriadas > 0)
        {
            await context.SaveChangesAsync();
        }

        return copiasCriadas;
    }

    public async Task TransferirValidadeAsync(int validadeId, int lojaDestinoId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var reg = await context.RegistrosValidade.FindAsync(validadeId);
        if (reg != null && reg.LojaId != lojaDestinoId)
        {
            reg.LojaId = lojaDestinoId;
            await context.SaveChangesAsync();
        }
    }

    public async Task AtualizarNomeProdutoAsync(int produtoId, string novoNome)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var p = await context.Produtos.FindAsync(produtoId);
        if (p != null && !string.IsNullOrWhiteSpace(novoNome))
        {
            p.Nome = novoNome.Trim();
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<RegistroValidade>> GetValidadesAtivasAsync(int? lojaId, string filtroRapido, DateTime? dataInicio = null, DateTime? dataFim = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var query = context.RegistrosValidade
            .Include(r => r.Produto)
            .Include(r => r.Loja)
            .Where(r => r.Status == "Ativo");

        if (lojaId.HasValue && lojaId.Value > 0)
        {
            query = query.Where(r => r.LojaId == lojaId.Value);
        }

        var hoje = DateTime.Today;

        query = (filtroRapido ?? "").ToLowerInvariant() switch
        {
            "7dias" => query.Where(r => r.DataValidade >= hoje && r.DataValidade <= hoje.AddDays(7)),
            "10dias" => query.Where(r => r.DataValidade >= hoje && r.DataValidade <= hoje.AddDays(10)),
            "15dias" => query.Where(r => r.DataValidade >= hoje && r.DataValidade <= hoje.AddDays(15)),
            "vencidos" => query.Where(r => r.DataValidade < hoje),
            "personalizado" when dataInicio.HasValue && dataFim.HasValue =>
                query.Where(r => r.DataValidade >= dataInicio.Value.Date && r.DataValidade <= dataFim.Value.Date),
            _ => query
        };

        return await query.OrderBy(r => r.DataValidade).ToListAsync();
    }

    public async Task AlternarPromocaoAsync(int validadeId, bool emPromocao)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var reg = await context.RegistrosValidade.FindAsync(validadeId);
        if (reg != null)
        {
            reg.EmPromocao = emPromocao;
            await context.SaveChangesAsync();
        }
    }

    public async Task DarBaixaAsync(int validadeId, string motivo)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var reg = await context.RegistrosValidade.FindAsync(validadeId);
        if (reg != null)
        {
            reg.Status = "Baixado";
            reg.DataBaixa = DateTime.Now;
            reg.MotivoBaixa = string.IsNullOrWhiteSpace(motivo) ? "Descarte/Vencido" : motivo.Trim();
            await context.SaveChangesAsync();
        }
    }

    public async Task DesfazerBaixaAsync(int validadeId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var reg = await context.RegistrosValidade.FindAsync(validadeId);
        if (reg != null)
        {
            reg.Status = "Ativo";
            reg.DataBaixa = null;
            reg.MotivoBaixa = null;
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<RegistroValidade>> GetHistoricoBaixasAsync(int? lojaId, DateTime? dataInicio = null, DateTime? dataFim = null, string? motivo = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var query = context.RegistrosValidade
            .Include(r => r.Produto)
            .Include(r => r.Loja)
            .Where(r => r.Status == "Baixado");

        if (lojaId.HasValue && lojaId.Value > 0)
        {
            query = query.Where(r => r.LojaId == lojaId.Value);
        }

        if (dataInicio.HasValue)
        {
            query = query.Where(r => r.DataBaixa != null && r.DataBaixa.Value.Date >= dataInicio.Value.Date);
        }

        if (dataFim.HasValue)
        {
            query = query.Where(r => r.DataBaixa != null && r.DataBaixa.Value.Date <= dataFim.Value.Date);
        }

        if (!string.IsNullOrWhiteSpace(motivo) && motivo != "Todos")
        {
            query = query.Where(r => r.MotivoBaixa == motivo);
        }

        return await query.OrderByDescending(r => r.DataBaixa).ToListAsync();
    }

    public string GerarCsvHistorico(IEnumerable<RegistroValidade> registros)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Loja;Código de Barras;Produto;Data Validade;Data Coleta;Data da Baixa;Motivo;Em Promoção");

        foreach (var item in registros)
        {
            var loja = SanitizarCampoCsv(item.Loja?.Nome);
            var codBarras = SanitizarCampoCsv(item.Produto?.CodigoBarras);
            var nome = SanitizarCampoCsv(item.Produto?.Nome);
            var validade = item.DataValidade.ToString("dd/MM/yyyy");
            var coleta = item.DataColeta.ToString("dd/MM/yyyy");
            var baixa = item.DataBaixa?.ToString("dd/MM/yyyy HH:mm:ss") ?? "N/D";
            var motivo = SanitizarCampoCsv(item.MotivoBaixa);
            var promo = item.EmPromocao ? "Sim" : "Não";

            sb.AppendLine($"{loja};{codBarras};{nome};{validade};{coleta};{baixa};{motivo};{promo}");
        }

        return sb.ToString();
    }

    private static string SanitizarCampoCsv(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "N/D";
        var tratado = valor.Replace(";", ",").Trim();
        // Previne CSV Formula Injection (DDE commands no Excel/Calc/Sheets)
        if (tratado.Length > 0 && (tratado[0] == '=' || tratado[0] == '+' || tratado[0] == '-' || tratado[0] == '@' || tratado[0] == '\t' || tratado[0] == '\r'))
        {
            tratado = "'" + tratado;
        }
        return tratado;
    }

    public async Task<List<ItemImportacaoPlanilha>> ProcessarPreviaPlanilhaAsync(Stream stream, string nomeArquivo, int lojaDestinoId)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.Position = 0;

        var ext = Path.GetExtension(nomeArquivo)?.ToLowerInvariant();
        var rows = new List<IDictionary<string, object>>();

        if (ext == ".csv")
        {
            ms.Position = 0;
            var buffer = ms.ToArray();
            string conteudo;
            try
            {
                var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                conteudo = utf8Strict.GetString(buffer);
            }
            catch (DecoderFallbackException)
            {
                conteudo = Encoding.Latin1.GetString(buffer);
            }

            using var reader = new StringReader(conteudo);
            var headerLine = await reader.ReadLineAsync();
            if (headerLine != null)
            {
                headerLine = headerLine.TrimStart('\uFEFF');
                char sep = headerLine.Contains(';') ? ';' : ',';
                var headers = headerLine.Split(sep).Select(h => h.Trim(' ', '"', '\r', '\n')).ToArray();
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(sep).Select(p => p.Trim(' ', '"', '\r', '\n')).ToArray();
                    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < headers.Length && i < parts.Length; i++)
                    {
                        dict[headers[i]] = parts[i];
                    }
                    rows.Add(dict);
                }
            }
        }
        else
        {
            // Arquivo Excel (.xlsx)
            ms.Position = 0;
            try
            {
                rows = ms.Query(useHeaderRow: true, excelType: ExcelType.XLSX)
                    .Cast<IDictionary<string, object>>()
                    .ToList();
            }
            catch
            {
                ms.Position = 0;
                rows = ms.Query(useHeaderRow: true)
                    .Cast<IDictionary<string, object>>()
                    .ToList();
            }
        }

        var itens = new List<ItemImportacaoPlanilha>();
        int linhaIndex = 2;

        await using var context = await _contextFactory.CreateDbContextAsync();
        var produtosExistentes = await context.Produtos
            .Select(p => new { p.Id, p.CodigoBarras })
            .ToDictionaryAsync(p => p.CodigoBarras, p => p.Id);

        var validadesExistentes = await context.RegistrosValidade
            .Where(v => v.LojaId == lojaDestinoId && v.Status == "Ativo")
            .Select(v => new { v.ProdutoId, v.DataValidade })
            .ToListAsync();

        foreach (IDictionary<string, object> row in rows)
        {
            string? nome = null;
            string? codigo = null;
            DateTime? validade = null;
            string? lojaOrigem = null;
            string? statusOrigem = null;

            foreach (var kvp in row)
            {
                var keyNorm = NormalizarChave(CorrigirEncodingSeNecessario(kvp.Key));
                var valStr = kvp.Value?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(valStr)) continue;

                if (nome == null && (keyNorm.Contains("nome") || keyNorm.Contains("produto") || keyNorm.Contains("descricao")))
                {
                    nome = CorrigirEncodingSeNecessario(valStr);
                }
                else if (codigo == null && (keyNorm.Contains("cod") || keyNorm.Contains("ean") || keyNorm.Contains("barr") || (keyNorm.StartsWith("c") && keyNorm.Contains("d"))))
                {
                    codigo = valStr;
                }
                else if (validade == null && (keyNorm.Contains("venc") || keyNorm.Contains("validade") || keyNorm.Contains("data")))
                {
                    validade = ConverterData(kvp.Value);
                }
                else if (lojaOrigem == null && keyNorm.Contains("loja"))
                {
                    lojaOrigem = valStr;
                }
                else if (statusOrigem == null && keyNorm.Contains("status"))
                {
                    statusOrigem = valStr;
                }
            }

            if (!string.IsNullOrWhiteSpace(codigo) || !string.IsNullOrWhiteSpace(nome))
            {
                var item = new ItemImportacaoPlanilha
                {
                    Linha = linhaIndex,
                    CodigoBarras = codigo ?? string.Empty,
                    NomeProduto = nome ?? string.Empty,
                    DataValidade = validade,
                    LojaOriginal = lojaOrigem,
                    StatusOriginal = statusOrigem
                };

                if (string.IsNullOrWhiteSpace(item.CodigoBarras))
                {
                    item.Valido = false;
                    item.MotivoInvalido = "Código de barras ausente";
                }
                else if (string.IsNullOrWhiteSpace(item.NomeProduto))
                {
                    item.Valido = false;
                    item.MotivoInvalido = "Nome do produto ausente";
                }
                else if (!item.DataValidade.HasValue)
                {
                    item.Valido = false;
                    item.MotivoInvalido = "Data de validade inválida ou ausente";
                }
                else
                {
                    item.Valido = true;
                    if (produtosExistentes.TryGetValue(item.CodigoBarras, out var prodId))
                    {
                        item.ProdutoExiste = true;
                        item.ValidadeJaExisteNaLoja = validadesExistentes.Any(v => v.ProdutoId == prodId && v.DataValidade.Date == item.DataValidade.Value.Date);
                    }
                }

                itens.Add(item);
            }

            linhaIndex++;
        }

        return itens;
    }

    public async Task<ResultadoImportacao> ExecutarImportacaoAsync(int lojaDestinoId, List<ItemImportacaoPlanilha> itens)
    {
        var resultado = new ResultadoImportacao
        {
            TotalLidos = itens.Count
        };

        var itensValidos = itens.Where(i => i.Valido && i.DataValidade.HasValue).ToList();
        if (!itensValidos.Any())
        {
            resultado.Sucesso = false;
            resultado.Mensagem = "Nenhum item válido para importar.";
            return resultado;
        }

        // Garante que migrações de banco pendentes (como remoção de LojaId legada em Produtos) foram executadas
        await InicializarBancoESeedAsync();

        await using var context = await _contextFactory.CreateDbContextAsync();

        // Identifica e cria produtos no catálogo compartilhado em lotes (evitando limite de parâmetros do SQLite)
        var codigos = itensValidos
            .Select(i => (i.CodigoBarras ?? string.Empty).Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var produtosNoBanco = new Dictionary<string, Produto>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in codigos.Chunk(500))
        {
            var chunkList = chunk.ToList();
            var doBanco = await context.Produtos
                .Where(p => chunkList.Contains(p.CodigoBarras))
                .ToListAsync();

            foreach (var p in doBanco)
            {
                produtosNoBanco[p.CodigoBarras] = p;
            }
        }

        var hoje = DateTime.Today;

        foreach (var item in itensValidos)
        {
            var codLimpo = (item.CodigoBarras ?? string.Empty).Trim();
            if (codLimpo.Length > 100) codLimpo = codLimpo.Substring(0, 100);

            var nomeLimpo = (item.NomeProduto ?? string.Empty).Trim();
            if (nomeLimpo.Length > 250) nomeLimpo = nomeLimpo.Substring(0, 250);

            if (string.IsNullOrEmpty(codLimpo)) continue;

            if (!produtosNoBanco.TryGetValue(codLimpo, out var produto))
            {
                produto = new Produto
                {
                    CodigoBarras = codLimpo,
                    Nome = string.IsNullOrEmpty(nomeLimpo) ? $"Produto {codLimpo}" : nomeLimpo
                };
                context.Produtos.Add(produto);
                produtosNoBanco[codLimpo] = produto;
                resultado.ProdutosCadastrados++;
            }
            else
            {
                // Se o nome no banco for genérico ou curto e a planilha trouxer um nome melhor
                if (!string.IsNullOrWhiteSpace(nomeLimpo) && nomeLimpo.Length > produto.Nome.Length)
                {
                    produto.Nome = nomeLimpo;
                    resultado.ProdutosAtualizados++;
                }
            }
        }

        await context.SaveChangesAsync();

        // Carrega validades existentes na loja de destino para evitar duplicatas ativas com mesma data
        var produtosIds = produtosNoBanco.Values.Select(p => p.Id).Distinct().ToList();
        var validadesJaAdicionadas = new HashSet<(int ProdutoId, DateTime DataValidade)>();

        foreach (var chunk in produtosIds.Chunk(500))
        {
            var chunkList = chunk.ToList();
            var validadesExistentes = await context.RegistrosValidade
                .Where(v => v.LojaId == lojaDestinoId && chunkList.Contains(v.ProdutoId) && v.Status == "Ativo")
                .Select(v => new { v.ProdutoId, v.DataValidade })
                .ToListAsync();

            foreach (var v in validadesExistentes)
            {
                validadesJaAdicionadas.Add((v.ProdutoId, v.DataValidade.Date));
            }
        }

        foreach (var item in itensValidos)
        {
            var codLimpo = (item.CodigoBarras ?? string.Empty).Trim();
            if (codLimpo.Length > 100) codLimpo = codLimpo.Substring(0, 100);

            if (!produtosNoBanco.TryGetValue(codLimpo, out var produto)) continue;
            var dataVal = item.DataValidade!.Value.Date;

            if (validadesJaAdicionadas.Contains((produto.Id, dataVal)))
            {
                resultado.ValidadesIgnoradasDuplicadas++;
                continue;
            }

            context.RegistrosValidade.Add(new RegistroValidade
            {
                ProdutoId = produto.Id,
                LojaId = lojaDestinoId,
                DataColeta = hoje, // Fixada com a data de hoje!
                DataValidade = dataVal,
                EmPromocao = false,
                Status = "Ativo"
            });

            validadesJaAdicionadas.Add((produto.Id, dataVal));
            resultado.ValidadesInseridas++;
        }

        await context.SaveChangesAsync();

        resultado.LinhasInvalidas = itens.Count(i => !i.Valido);
        resultado.Sucesso = true;
        resultado.Mensagem = $"Importação realizada com sucesso! {resultado.ValidadesInseridas} validade(s) inserida(s) e {resultado.ProdutosCadastrados} novo(s) produto(s) cadastrado(s).";

        return resultado;
    }

    private static DateTime? ConverterData(object? valor)
    {
        if (valor == null) return null;
        if (valor is DateTime dt) return dt.Date;
        if (valor is double d)
        {
            try { return DateTime.FromOADate(d).Date; } catch { }
        }
        var str = valor.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(str)) return null;

        string[] formatos = { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "yyyy-MM-dd", "yyyy/MM/dd", "dd-MM-yyyy" };
        if (DateTime.TryParseExact(str, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var resExact))
        {
            return resExact.Date;
        }
        if (DateTime.TryParse(str, new CultureInfo("pt-BR"), DateTimeStyles.None, out var resPtBr))
        {
            return resPtBr.Date;
        }
        return null;
    }

    private static readonly Encoding Win1252Encoding = ObterWindows1252();

    private static Encoding ObterWindows1252()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
        catch
        {
            return Encoding.Latin1;
        }
    }

    private static string CorrigirEncodingSeNecessario(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        if (texto.Contains('Ã') || texto.Contains('Â') || texto.Contains('â'))
        {
            try
            {
                var bytes = Win1252Encoding.GetBytes(texto);
                var redecoded = Encoding.UTF8.GetString(bytes);
                if (!redecoded.Contains('\uFFFD'))
                {
                    return redecoded;
                }
            }
            catch
            {
                // se falhar, mantém original
            }
        }
        return texto;
    }

    private static string NormalizarChave(string? chave)
    {
        if (string.IsNullOrEmpty(chave)) return string.Empty;
        var norm = chave.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in norm)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}

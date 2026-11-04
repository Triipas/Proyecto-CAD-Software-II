using System.Text.Json;
using Microsoft.ML;

namespace Cad.Proyeccion;

// HU48 (plan, 8.3 y 8.5) — el modelo de aprendizaje automático: árboles de decisión con objetivo de conteo (FastTree con pérdida de
// Tweedie, adecuada para conteos con muchos ceros). Se entrena fuera del servidor (herramienta ia-evaluacion) y el servidor solo lo
// carga para predecir. Con el modelo viaja el peso del conjunto con la línea base, elegido en el backtest.
public sealed record MetadatosModelo(string Version, double PesoModelo, string Mundo, int Semanas, DateTimeOffset EntrenadoEn, string[] Rasgos);

public sealed class ModeloFocos
{
    private readonly MLContext ml;
    private readonly ITransformer modelo;
    private readonly DataViewSchema esquema;
    private readonly Lock cerrojo = new();
    // PredictionEngine no es seguro entre hilos (plan, 4.6): uno solo, con cerrojo; predecir 80 celdas tarda milisegundos.
    private PredictionEngine<FilaRasgos, PrediccionRasgos>? motor;

    private ModeloFocos(MLContext ml, ITransformer modelo, DataViewSchema esquema, MetadatosModelo metadatos)
    {
        this.ml = ml;
        this.modelo = modelo;
        this.esquema = esquema;
        Metadatos = metadatos;
    }

    public MetadatosModelo Metadatos { get; }

    public static ModeloFocos Entrenar(IEnumerable<FilaRasgos> filas, MetadatosModelo metadatos, int semilla = 20261104)
    {
        var ml = new MLContext(semilla);
        var datos = ml.Data.LoadFromEnumerable(filas);
        var tuberia = ml.Transforms.Concatenate("Features", FilaRasgos.Nombres)
            .Append(ml.Regression.Trainers.FastTreeTweedie(labelColumnName: nameof(FilaRasgos.Label), featureColumnName: "Features",
                numberOfLeaves: 31, numberOfTrees: 200, minimumExampleCountPerLeaf: 50, learningRate: 0.05));
        var modelo = tuberia.Fit(datos);
        return new ModeloFocos(ml, modelo, datos.Schema, metadatos);
    }

    public float[] Predecir(IReadOnlyList<FilaRasgos> filas)
    {
        lock (cerrojo)
        {
            motor ??= ml.Model.CreatePredictionEngine<FilaRasgos, PrediccionRasgos>(modelo);
            // FastTreeTweedie ya devuelve casos esperados (no el logaritmo): comprobado con la calibración de «entrenar».
            return filas.Select(f => Math.Max(0, motor.Predict(f).Casos)).ToArray();
        }
    }

    // Un .zip de ML.NET con un metadatos.json adentro.
    public void Guardar(string ruta)
    {
        using (var archivo = File.Create(ruta))
            ml.Model.Save(modelo, esquema, archivo);
        using var zip = System.IO.Compression.ZipFile.Open(ruta, System.IO.Compression.ZipArchiveMode.Update);
        var entrada = zip.CreateEntry("metadatos.json");
        using var escritor = new StreamWriter(entrada.Open());
        escritor.Write(JsonSerializer.Serialize(Metadatos, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static ModeloFocos Cargar(Stream flujo)
    {
        using var copia = new MemoryStream();
        flujo.CopyTo(copia);
        copia.Position = 0;
        MetadatosModelo metadatos;
        using (var zip = new System.IO.Compression.ZipArchive(copia, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
        {
            var entrada = zip.GetEntry("metadatos.json") ?? throw new InvalidDataException("El modelo no trae metadatos.json.");
            using var lector = new StreamReader(entrada.Open());
            metadatos = JsonSerializer.Deserialize<MetadatosModelo>(lector.ReadToEnd())!;
        }
        copia.Position = 0;
        var ml = new MLContext(0);
        var modelo = ml.Model.Load(copia, out var esquema);
        return new ModeloFocos(ml, modelo, esquema, metadatos);
    }
}

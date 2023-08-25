using Microsoft.Extensions.Configuration;
using CosmosMovieGuide;
using Spectre.Console;
using Console = Spectre.Console.AnsiConsole;
using System.Net;
using CosmosMovieGuide.Services;
using System.Net.Quic;
using System.Diagnostics;
using Newtonsoft.Json;
using Azure.Search.Documents;


namespace CosmosMovieGuide
{
    internal class Program
    {

        static CosmosDbService cosmosService=null;
        static SemanticKernelService skService =null; 
        
        //static OpenAIService openAIEmbeddingService = null;
        //static CognitiveSearchService cogSearchService = null;

        static async Task Main(string[] args)
        {
            AnsiConsole.Write(
               new FigletText("Contoso Movie FAQ")
               .Color(Color.Red));

            Console.WriteLine("");

            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true);

            var config = configuration.Build();

            
            cosmosService = initCosmosDBService(config);
            

            const string cosmosUpload = "1.\tUpload movie(s) to Cosmos DB";
            const string vectorize = "2.\tVectorize the movies(s) and store it in Cosmos DB";
            const string search = "3.\tAsk AI Assistant questions about movies";
            const string exit = "4.\tExit this Application";


            while (true)
            {

                var selectedOption = AnsiConsole.Prompt(
                      new SelectionPrompt<string>()
                          .Title("Select an option to continue")
                          .PageSize(10)
                          .MoreChoicesText("[grey](Move up and down to reveal more options)[/]")
                          .AddChoices(new[] {
                            cosmosUpload,vectorize ,search, exit
                          }));


                switch (selectedOption)
                {
                    case cosmosUpload:
                        UploadMovies(config);
                        break;
                    case vectorize:
                        GenerateEmbeddings(config);                        
                        break;
                    case search:
                        PerformSearch(config);
                        break;
                    case exit:
                        return;                        
                }
            }       
                 
        }


        private static SemanticKernelService initSKService(IConfiguration config)
        {
            string OpenAIEndpoint = config["OpenAIEndpoint"];
            string OpenAIKey = config["OpenAIKey"];
            string EmbeddingDeployment = config["OpenAIEmbeddingDeployment"];
            string CompletionsDeployment = config["OpenAIcompletionsDeployment"];

            string ACSEndpoint= config["SearchServiceEndPoint"];
            string ACSAdmiKey = config["SearchServiceAdminApiKey"];

            return new SemanticKernelService(OpenAIEndpoint, OpenAIKey, EmbeddingDeployment, CompletionsDeployment, ACSEndpoint, ACSAdmiKey);
        }


        private static CosmosDbService initCosmosDBService( IConfiguration config)
        {
            CosmosDbService cosmosService=null;

            string endpoint = config["CosmosUri"];
            string key = config["CosmosKey"];
            string databaseName = config["CosmosDatabase"];
            string containerName = config["CosmosContainer"];
            
            
            int movieWithEmbedding = 0;
            int movieWithNoEmbedding = 0;

            AnsiConsole.Status()
                .Start("Processing...", ctx =>
                {
                    
                    ctx.Spinner(Spinner.Known.Star);
                    ctx.SpinnerStyle(Style.Parse("green"));

                    ctx.Status("Creating Cosmos DB Client ..");
                    cosmosService = new CosmosDbService(endpoint, key, databaseName, containerName);

                    ctx.Status("Getting Movie Stats");
                    movieWithEmbedding = cosmosService.GetMovieCountAsync(true).GetAwaiter().GetResult();
                    movieWithNoEmbedding = cosmosService.GetMovieCountAsync(false).GetAwaiter().GetResult();                                       
                    

                });

            AnsiConsole.MarkupLine($"We have [green]{movieWithEmbedding}[/] vectorized movie(s) and [red]{movieWithNoEmbedding}[/] non vectorized movie(s).");
            Console.WriteLine("");

            return cosmosService;

        }


        private static void UploadMovies(IConfiguration config)
        {
            string folder = config["MovieLocalFolder"];
            int movieWithEmbedding = 0;
            int movieWithNoEmbedding = 0;

            List<Movie> movies=null;

            AnsiConsole.Status()
               .Start("Processing...", ctx =>
               {
                   ctx.Spinner(Spinner.Known.Star);
                   ctx.SpinnerStyle(Style.Parse("green"));

                   ctx.Status("Parsing Movie files..");
                   movies = Utility.ParseDocuments(folder);                  
                  
                   ctx.Status($"Uploading Movie(s)..");
                   cosmosService.AddMoviesAsync(movies).GetAwaiter().GetResult();

                   ctx.Status("Getting Updated <Movie> Stats");
                   movieWithEmbedding = cosmosService.GetMovieCountAsync(true).GetAwaiter().GetResult();
                   movieWithNoEmbedding = cosmosService.GetMovieCountAsync(false).GetAwaiter().GetResult();

               });

            AnsiConsole.MarkupLine($"Uploaded [green]{movies.Count}[/] movie(s).We have [teal]{movieWithEmbedding}[/] vectorized movie(s) and [red]{movieWithNoEmbedding}[/] non vectorized movie(s).");
            Console.WriteLine("");

        }
        

       private static void PerformSearch(IConfiguration config)
        {
            Dictionary<string, float[]> docsToUpdate = new Dictionary<string, float[]>();

            string chatCompletion=string.Empty;

            string userQuery = Console.Prompt(
                new TextPrompt<string>("Type the movie name or your question, hit enter when ready.")
                    .PromptStyle("teal")
            );

            
            AnsiConsole.Status()
               .Start("Processing...", ctx =>
               {
                   ctx.Spinner(Spinner.Known.Star);
                   ctx.SpinnerStyle(Style.Parse("green"));

                   if (skService == null)
                   {
                       ctx.Status("Initializing Semantic Kernel Service..");
                       skService = initSKService(config);
                   }

                   ctx.Status("Performing Vector Search..");
                   var ids= skService.SearchEmbeddingsAsync(userQuery).GetAwaiter().GetResult();

                   ctx.Status("Retriving movie(s) from Cosmos DB (RAG pattern)..");
                   var retrivedDocs=cosmosService.GetMoviesAsync(ids).GetAwaiter().GetResult();

                   ctx.Status($"Priocessing {retrivedDocs.Count} to generate Chat Response using OpenAI Service..");

                   string retrivedMovieNames = string.Empty;
                   
                   foreach(var movie in retrivedDocs)
                   {
                       retrivedMovieNames += ", " + movie.name; //to dispay movies submitted for Completion
                   }

                   ctx.Status($"Processing '{retrivedMovieNames}' to generate Completion using OpenAI Service..");

                   chatCompletion = skService.GenerateCompletionAsync(userQuery, JsonConvert.SerializeObject(retrivedDocs)).GetAwaiter().GetResult();

   
               });

            Console.WriteLine("");
            Console.Write(new Rule($"[silver]AI Assistant Response[/]") { Justification = Justify.Center });
            AnsiConsole.MarkupLine(chatCompletion);
            Console.WriteLine("");
            Console.WriteLine("");
            Console.Write(new Rule($"[yellow]****[/]") { Justification = Justify.Center });
            Console.WriteLine("");

        }

        private static void GenerateEmbeddings(IConfiguration config)
        {
            List<string> docsToUpdate = new List<string>();
            int movieWithEmbedding = 0;
            int movieWithNoEmbedding = 0;
            int movieCount = 0;

            AnsiConsole.Status()
               .Start("Processing...", ctx =>
               {
                   ctx.Spinner(Spinner.Known.Star);
                   ctx.SpinnerStyle(Style.Parse("green"));

                   if (skService == null)
                   {
                       ctx.Status("Initializing Semantic Kernel Service..");
                       skService = initSKService(config);
                   }


                   ctx.Status("Getting movie(s) to vectorize..");
                   var Movies = cosmosService.GetMoviesToVectorizeAsync().GetAwaiter().GetResult();
                                      
                   foreach ( var movie in Movies ) 
                   {
                       movieCount++;
                       ctx.Status($"Vectorizing Movie# {movieCount}..");
                       skService.SaveEmbeddingsAsync(JsonConvert.SerializeObject(movie),movie.id).GetAwaiter().GetResult();
                       docsToUpdate.Add(movie.id);
                   }

                   ctx.Status($"Updating {Movies.Count} movie(s) in Cosmos DB for vectors..");
                   cosmosService.UpdateMoviesAsync(docsToUpdate).GetAwaiter().GetResult();

                   ctx.Status("Getting Updated Movie Stats");
                   movieWithEmbedding = cosmosService.GetMovieCountAsync(true).GetAwaiter().GetResult();
                   movieWithNoEmbedding = cosmosService.GetMovieCountAsync(false).GetAwaiter().GetResult();

               });

            AnsiConsole.MarkupLine($"Vectorized [teal]{movieCount}[/] movie(s). We have [green]{movieWithEmbedding}[/] vectorized movie(s) and [red]{movieWithNoEmbedding}[/] non vectorized movie(s).");
            Console.WriteLine("");

        }

       

    }
}
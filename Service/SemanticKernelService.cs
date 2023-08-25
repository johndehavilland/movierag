using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Azure.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AI.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Memory.AzureCognitiveSearch;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.TemplateEngine;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CosmosMovieGuide.Services
{
    public class SemanticKernelService
    {
        IKernel kernel;
        private const string MemoryCollectionName = "SKMovies";

        public SemanticKernelService(string OpenAIEndpoint, string OpenAIKey, string EmbeddingsDeployment, string CompletionDeployment, string ACSEndpoint, string ACSApiKey)
        {
            // IMPORTANT: Register an embedding generation service and a memory store using  Azure Cognitive Service. 
            kernel = new KernelBuilder()
                .WithAzureChatCompletionService(
                    CompletionDeployment,
                    OpenAIEndpoint,
                    OpenAIKey)
                .WithAzureTextEmbeddingGenerationService(
                    EmbeddingsDeployment,
                    OpenAIEndpoint,
                    OpenAIKey)
                .WithMemoryStorage(new AzureCognitiveSearchMemoryStore(ACSEndpoint, ACSApiKey))
                .Build();
        }


        public async Task SaveEmbeddingsAsync(string data, string id)
        {
            try
            {
                await kernel.Memory.SaveReferenceAsync(
                   collection: MemoryCollectionName,
                   externalSourceName: "movies",
                   externalId: id,
                   description: data,
                   text: data);

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());   
            }
        }

        public async Task<List<string>> SearchEmbeddingsAsync(string query)
        {
            var memories = kernel.Memory.SearchAsync(MemoryCollectionName, query, limit: 2, minRelevanceScore: 0.5);

            List<string> result = new List<string>();   
            int i = 0;
            await foreach (MemoryQueryResult memory in memories)
            {
                result.Add(memory.Metadata.Id);
            }
            return result;
        }

        public async Task<string> GenerateCompletionAsync(string userPrompt, string  movieData)
        {
            string systemPromptMovieAssistant = @"
            You are an intelligent assistant for Contoso Movie FAQ. 
            You are designed to provide helpful answers to user questions about movies using the provided JSON strings
            INSTRUCTIONS
            - In case a movie is not provided in the prompt politely refuse to answer all queries regarding it. 
            - Never refer to a movie not provided as input to you.
            - If you're unsure of an answer, you can say ""I don't know"" or ""I'm not sure"" and recommend users search themselves.        
            - Your response  should be complete. 
            - Format the content so that it can be printed to the Command Line";

            // Client used to request answers to gpt-3.5 - turbo
            var chatCompletion = kernel.GetService<IChatCompletion>();

            var chatHistory = chatCompletion.CreateNewChat(systemPromptMovieAssistant);

            // add shortlisted movies as system message
            chatHistory.AddSystemMessage(movieData);

            // send user promt as user message
            chatHistory.AddUserMessage(userPrompt);

            // Finally, get the response from AI
            string answer = await chatCompletion.GenerateMessageAsync(chatHistory);


            return answer;
        }
    }
}

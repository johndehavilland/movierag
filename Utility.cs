using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;

namespace CosmosMovieGuide
{
    internal static class Utility
    {
        public static List<Movie> ParseDocuments(string Folderpath)
        {
            List<Movie> ret = new List<Movie>();

            Directory.GetFiles(Folderpath).ToList().ForEach(f =>
                {
                    var jsonString= System.IO.File.ReadAllText(f);
                    Movie movie = JsonConvert.DeserializeObject<Movie>(jsonString);
                    movie.id = movie.name.ToLower().Replace(" ","");
                    movie.embeddingStatus = false;
                    ret.Add(movie);

                }
            );


            return ret;

        }
    }
}

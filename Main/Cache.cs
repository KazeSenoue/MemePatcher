﻿using System;
using System.IO;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Threading.Tasks;

namespace Main
{
    class Cache
    {
        [JsonRequired]
        [JsonProperty("file")]
        public string File { get; set; }

        [JsonRequired]
        [JsonProperty("MD5")]
        public string MD5 { get; set; }

        [JsonRequired]
        [JsonProperty("lastModified")]
        public long? LastModified { get; set; }

        private static Properties.Settings _Settings = Properties.Settings.Default;

        public Cache(string File, string MD5 = null, long? LastModified = null)
        {
            this.File = File;
            this.MD5 = MD5;
            this.LastModified = LastModified;
        }

        public static async Task<string> CalculateHash(string file, System.Security.Cryptography.MD5 md5)
        {
            Task<string> MD5 = Task.Run(() =>
            {
                {
                    using (var stream = new BufferedStream(System.IO.File.OpenRead(file), 1200000))
                    {
                        var hash = md5.ComputeHash(stream);
                        var fileMD5 = string.Concat(Array.ConvertAll(hash, x => x.ToString("X2")));

                        return fileMD5;
                    }
                };
            });

            return await MD5;
        }

        public static async Task UpdateCache(string directory, string file)
        {
            var fullPath = Path.Combine(directory, file);
            var cache = JsonConvert.DeserializeObject<List<Cache>>(System.IO.File.ReadAllText("cache.json"));
            int index = cache.FindIndex(f => f.File == file);
            string hash;
            long lastModified;

            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                hash = await CalculateHash(fullPath, md5);
                lastModified = new DateTimeOffset(new FileInfo(fullPath).LastWriteTimeUtc).ToUnixTimeMilliseconds();
            }

            if (index >= 0)
            {
                cache[index].MD5 = hash;
                cache[index].LastModified = lastModified;
            }
            else
            {
                cache.Add(new Cache(file, hash, lastModified));
            }

            Debug.WriteLine($"Cache: {file} / Last modified: {lastModified} / Hash: {hash}");
            System.IO.File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "cache.json"), JsonConvert.SerializeObject(cache));
        }

        public static List<Cache> ReadCache(string directory)
        {
            return JsonConvert.DeserializeObject<List<Cache>>(System.IO.File.ReadAllText(directory));
        }

        public static async Task BuildCache(string directory)
        {
            var fileList = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories).Select(f => new FileInfo(f)).ToList();
            List<Cache> cacheFileList = new List<Cache>();
            List<string> blacklist = new List<string>() { "GameGuard", "plugins", "damagedump", ".bak", "tweaker", "gameversion", "pso2h", "titles", "translation", ".bat", ".json" };
            int i = 0;

            foreach (var file in fileList)
            {
                i++;
                var filename = file.FullName.Substring(directory.Length + 1);

                if (!blacklist.Any(filename.Contains))
                {
                    using (var md5 = System.Security.Cryptography.MD5.Create())
                    {
                        cacheFileList.Add(new Cache(filename, await CalculateHash(file.FullName, md5), new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds()));
                    }
                }

                MainWindow.UpdateLabel($"{i} / {fileList.Count()} - {(double)(100 * i) / fileList.Count():F3}%");
                MainWindow.UpdateProgressBar((100 * i) / fileList.Count());
            }

            System.IO.File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "cache.json"), JsonConvert.SerializeObject(cacheFileList));
            Process.Start(System.Windows.Application.ResourceAssembly.Location);
            System.Windows.Application.Current.Shutdown();
        }

        class FileCacheEntryNameComparer : IEqualityComparer<Cache>
        {
            public bool Equals(Cache x, Cache y) => x.File == y.File;
            public int GetHashCode(Cache obj) => obj.File.GetHashCode();
        }

        class FileCacheEntryLastModifiedComparer : IEqualityComparer<Cache>
        {
            public bool Equals(Cache x, Cache y) => x.LastModified == y.LastModified;
            public int GetHashCode(Cache obj) => obj.LastModified.GetHashCode();
        }

        public static Dictionary<string, List<Cache>> ReturnMissingFiles()
        {
            List<string> blacklist = new List<string>() { "GameGuard", "plugins", "damagelogs", ".bak", "tweaker", "gameversion", "pso2h", "titles", "translation", ".bat", ".json" };

            //Beggining of file check

            using (var client = new WebClient())
            {
                if (!Directory.Exists("Temp"))
                    Directory.CreateDirectory("Temp");

                client.Headers.Add("User-Agent", "AQUA_HTTP");
                var patchListStr = client.DownloadString("http://download.pso2.jp/patch_prod/patches/patchlist.txt")
                    .Split(new[] { "\r\n" }, StringSplitOptions.None)
                    .Select(item => item.Split())
                    .ToList()
                    .Where(f => f.Length == 3);

                client.Headers.Add("User-Agent", "AQUA_HTTP");
                var patchListStr_old = client.DownloadString("http://download.pso2.jp/patch_prod/patches_old/patchlist.txt")
                    .Split(new[] { "\n" }, StringSplitOptions.None)
                    .Select(item => item.Split())
                    .ToList()
                    .Where(f => f.Length == 3);

                List<Cache> patchList = patchListStr
                    .Select(f => new Cache(f[0].Replace(@"/", @"\").Replace(".pat", ""), f[2]))
                    .ToList();

                List<Cache> patchListOld = patchListStr_old
                    .Select(f => new Cache(f[0].Replace(@"/", @"\").Replace(".pat", ""), f[2]))
                    .ToList();

                List<Cache> fileList = patchList.Union(patchListOld.Except(patchList, new FileCacheEntryNameComparer())).ToList();

                List<Cache> localFiles = Directory.GetFiles(_Settings.Pso2Path, "*.*", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .Where(f => !blacklist.Any(f.FullName.Contains))
                    .Select(f => new Cache(f.FullName.Substring(_Settings.Pso2Path.Length + 1), null, new DateTimeOffset(f.LastWriteTimeUtc).ToUnixTimeMilliseconds()))
                    .ToList();

                List<Cache> localCache = Cache.ReadCache("cache.json").Select(i => new Cache(i.File, i.MD5, i.LastModified)).ToList();
                List<Cache> missingFiles = fileList.Except(localFiles, new FileCacheEntryNameComparer())
                    .Where(f => !blacklist.Any(f.File.Contains))
                    .ToList();

                List<Cache> modifiedFiles = localFiles.Except(localCache, new FileCacheEntryLastModifiedComparer())
                    .ToList();

                List<Cache> corruptFiles = modifiedFiles.Where(e =>
                {
                    using (var md5 = System.Security.Cryptography.MD5.Create())
                    using (var stream = new BufferedStream(System.IO.File.OpenRead(Path.Combine(_Settings.Pso2Path, e.File)), 1200000))
                    {
                        var hash = md5.ComputeHash(stream);
                        var fileMD5 = string.Concat(Array.ConvertAll(hash, x => x.ToString("X2")));

                        return fileMD5 != e.MD5;
                    }
                }).ToList();

                Debug.WriteLine("returning");
                return new Dictionary<string, List<Cache>>()
                {
                    {"missingFiles", missingFiles},
                    {"corruptFiles", corruptFiles}
                };
            }
        }
    }
}

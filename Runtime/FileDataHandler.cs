using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace SaveSystem
{
    /// <summary>
    /// Handles reading and writing of serializable data to disk or PlayerPrefs.
    /// Supports both synchronous and asynchronous operations.
    /// </summary>
    /// <typeparam name="T">The type of data to serialize. Must be a class.</typeparam>
    public class FileDataHandler<T> where T : class
    {
        private readonly string dataDirPath;
        private readonly string dataFileName;

        /// <summary>
        /// Creates a new handler targeting a specific directory and file name.
        /// </summary>
        /// <param name="dataDirPath">The directory where the file will be read from and written to.</param>
        /// <param name="dataFileName">The name of the file, including extension (e.g. "gameData.json").</param>
        public FileDataHandler(string dataDirPath, string dataFileName)
        {
            this.dataDirPath = dataDirPath;
            this.dataFileName = dataFileName;
        }

        /// <summary>
        /// Synchronously serializes <paramref name="data"/> and writes it to disk or PlayerPrefs.
        /// Prefer <see cref="SaveAsync"/> to avoid blocking the main thread.
        /// </summary>
        /// <param name="data">The data object to serialize.</param>
        /// <param name="usePlayerPrefs">If true, writes to PlayerPrefs instead of a file.</param>
        public void Save(T data, bool usePlayerPrefs = false)
        {
            if (usePlayerPrefs)
            {
                string dataToStore = JsonUtility.ToJson(data, true);
                PlayerPrefs.SetString(dataFileName, dataToStore);
            }
            else
            {
                string fullPath = Path.Combine(dataDirPath, dataFileName);

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? string.Empty);

                    string dataToStore = JsonUtility.ToJson(data, true);

                    using FileStream stream = new FileStream(fullPath, FileMode.Create);
                    using StreamWriter writer = new StreamWriter(stream);

                    writer.Write(dataToStore);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error while saving data to file: {fullPath} \n {e}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Synchronously reads and deserializes data from disk or PlayerPrefs.
        /// Returns null if no data is found.
        /// Prefer <see cref="LoadAsync"/> to avoid blocking the main thread.
        /// </summary>
        /// <param name="usePlayerPrefs">If true, reads from PlayerPrefs instead of a file.</param>
        /// <returns>The deserialized data, or null if no saved data exists.</returns>
        public T Load(bool usePlayerPrefs = false)
        {
            string fullPath = Path.Combine(dataDirPath, dataFileName);
            T loadedData = null;

            if (usePlayerPrefs)
            {
                string dataToLoad = PlayerPrefs.GetString(dataFileName, "");
                loadedData = JsonUtility.FromJson<T>(dataToLoad);
            }
            else
            {
                if (File.Exists(fullPath))
                {
                    try
                    {
                        using FileStream stream = new FileStream(fullPath, FileMode.Open);
                        using StreamReader reader = new StreamReader(stream);

                        string dataToLoad = reader.ReadToEnd();
                        loadedData = JsonUtility.FromJson<T>(dataToLoad);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error while loading data from file: {fullPath} \n {e}");
                        throw;
                    }
                }
            }

            return loadedData;
        }


        /// <summary>
        /// Asynchronously serializes <paramref name="data"/> and writes it to disk.
        /// Falls back to the synchronous path when using PlayerPrefs, as it has no async API.
        /// </summary>
        /// <param name="data">The data object to serialize.</param>
        /// <param name="usePlayerPrefs">If true, writes to PlayerPrefs instead of a file.</param>
        public async Task SaveAsync(T data, bool usePlayerPrefs = false)
        {
            if (usePlayerPrefs)
            {
                // PlayerPrefs has no async API, so we just call the sync version.
                Save(data, true);
                return;
            }

            string fullPath = Path.Combine(dataDirPath, dataFileName);

            try
            {
                Debug.Log($"[SaveAsync] Attempting to save to: {fullPath}");

                // Note: Directory creation is synchronous. It is fast enough to
                // run on the main thread in practice.
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? string.Empty);

                // JsonUtility is synchronous.
                // If the dataset is massive, consider using a different JSON library (like Newtonsoft).
                string dataToStore = JsonUtility.ToJson(data, true);

                await using FileStream stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write,
                    FileShare.ReadWrite, 4096, useAsync: true);
                await using StreamWriter writer = new StreamWriter(stream);

                await writer.WriteAsync(dataToStore);
                await writer.FlushAsync();
            }
            catch (IOException ioEx)
            {
                Debug.LogError($"[SaveAsync] IO Exception on path: {fullPath}\n" +
                               $"Message: {ioEx.Message}\n" +
                               $"HResult: {ioEx.HResult}\n" +
                               $"Stack: {ioEx.StackTrace}");
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error while saving data to file: {fullPath} \n {e}");
                throw;
            }
        }

        /// <summary>
        /// Asynchronously reads and deserializes data from disk.
        /// Falls back to the synchronous path when using PlayerPrefs, as it has no async API.
        /// Returns null if no saved data exists.
        /// </summary>
        /// <param name="usePlayerPrefs">If true, reads from PlayerPrefs instead of a file.</param>
        /// <returns>The deserialized data, or null if no saved data exists.</returns>
        public async Task<T> LoadAsync(bool usePlayerPrefs = false)
        {
            if (usePlayerPrefs)
            {
                return Load(true);
            }

            string fullPath = Path.Combine(dataDirPath, dataFileName);

            if (!File.Exists(fullPath))
            {
                Debug.LogWarning($"[LoadAsync] No save file found at: {fullPath}");
                return null;
            }

            try
            {
                string dataToLoad = await File.ReadAllTextAsync(fullPath);
                return JsonUtility.FromJson<T>(dataToLoad);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LoadAsync] Error while loading data from file: {fullPath}\n{e}");
                throw;
            }
        }
    }
}
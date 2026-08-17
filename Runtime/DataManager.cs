using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

namespace SaveSystem
{
    /// <summary>
    /// Manages saving and loading game data. Coordinates all <see cref="ISerializable"/> objects
    /// in the scene and delegates file I/O to <see cref="FileDataHandler{T}"/>.
    /// The caller is responsible for triggering saves on application pause and quit.
    /// </summary>
    public class DataManager<T> where T : SaveData, new()
    {
        readonly string dirPath;
        readonly string fileName;
        readonly bool usePlayerPrefs;
        readonly FileDataHandler<T> dataHandler;
        readonly HashSet<ISerializable> serializableObjects = new();

        T currentData;

        /// <summary>
        /// True once <see cref="currentData"/> reflects either a successful load from disk
        /// or an explicit <see cref="NewFileSave"/>. Guards <see cref="Save"/>/<see cref="SaveAsync"/>
        /// against writing fabricated blank data over a real save file — e.g. if a save is
        /// triggered (app pause/quit, an exception elsewhere) before load has completed.
        /// </summary>
        bool hasLoaded = false;

        /// <summary>
        /// The current save data loaded in memory. May be null if no load or new-file-save
        /// has happened yet — check <see cref="HasLoaded"/> before relying on this being populated.
        /// </summary>
        public T CurrentData => currentData;

        /// <summary>
        /// True once save data is safely established in memory (via successful load or
        /// explicit new-file-save) and it is safe to call <see cref="Save"/>/<see cref="SaveAsync"/>.
        /// </summary>
        public bool HasLoaded => hasLoaded;

        public DataManager(string dirPath, string fileName = "saveData.json", bool usePlayerPrefs = false)
        {
            this.dirPath = dirPath;
            this.fileName = fileName;
            this.usePlayerPrefs = usePlayerPrefs;

            dataHandler = new FileDataHandler<T>(this.dirPath, this.fileName);
        }

        /// <summary>
        /// Resets save data to its default values. This is the only path that should
        /// produce a blank <see cref="CurrentData"/> — call it explicitly when the player
        /// starts a new game, not as an implicit fallback during save.
        /// </summary>
        public void NewFileSave()
        {
            currentData = new T();
            hasLoaded = true;
        }

        /// <summary>
        /// Synchronously loads game data from disk or PlayerPrefs and applies it to all
        /// <see cref="ISerializable"/> objects in the scene.
        /// Prefer <see cref="LoadAsync"/> to avoid blocking the main thread.
        /// </summary>
        public void Load(bool scanScene = false)
        {
            currentData = dataHandler.Load(usePlayerPrefs);

            if (currentData == null)
            {
                // NOTE: FileDataHandler currently collapses "no file found" and
                // "file found but failed to parse/corrupted" into the same null result.
                // If you add a status-aware Load to FileDataHandler later, a corrupted
                // file should NOT fall through to NewFileSave() here — it should leave
                // hasLoaded false so Save() refuses to run and the game can surface a
                // recovery/error flow instead of silently destroying the corrupted file.
                Debug.LogWarning("[DataManager] No save file found. Initializing to defaults.");
                NewFileSave();
            }
            else
            {
                hasLoaded = true;
            }

            if (scanScene)
            {
                FindAllSerializableObjects();
            }

            ApplyDataToObjects();
        }

        /// <summary>
        /// Synchronously collects data from all <see cref="ISerializable"/> objects
        /// and writes it to disk or PlayerPrefs.
        /// Prefer <see cref="SaveAsync"/> to avoid blocking the main thread.
        /// Does nothing (and logs an error) if called before data has been loaded or
        /// initialized, to avoid overwriting an existing save with blank data.
        /// </summary>
        /// <param name="scanScene">
        /// If true, scans the current scene before saving.
        /// </param>
        /// <param name="clearDestroyed">
        /// If true, removes any destroyed objects from the serializable list before saving.
        /// </param>
        public void Save(bool scanScene = false, bool clearDestroyed = false)
        {
            if (!hasLoaded)
            {
                Debug.LogError("[DataManager] Save() called before data was loaded or initialized — aborting to avoid overwriting the save file with blank data.");
                return;
            }

            if (scanScene)
            {
                FindAllSerializableObjects();
            }

            PrepareDataForSaving(clearDestroyed);
            dataHandler.Save(currentData, usePlayerPrefs);
        }

        /// <summary>
        /// Asynchronously loads game data from disk or PlayerPrefs and applies it to all
        /// <see cref="ISerializable"/> objects in the scene.
        /// Data is applied on the main thread after loading completes.
        /// </summary>
        public async Task LoadAsync(bool scanScene = false)
        {
            currentData = await dataHandler.LoadAsync(usePlayerPrefs);

            if (currentData == null)
            {
                Debug.LogWarning($"[DataManager] No save file found at {dirPath}. Initializing to defaults.");
                NewFileSave();
            }
            else
            {
                hasLoaded = true;
            }

            if (scanScene)
            {
                FindAllSerializableObjects();
            }

            ApplyDataToObjects();
        }

        /// <summary>
        /// Asynchronously collects data from all <see cref="ISerializable"/> objects
        /// and writes it to disk or PlayerPrefs.
        /// Does nothing (and logs an error) if called before data has been loaded or
        /// initialized, to avoid overwriting an existing save with blank data.
        /// </summary>
        /// <param name="scanScene">
        /// If true, scans the current scene before saving.
        /// </param>
        /// <param name="clearDestroyed">
        /// If true, removes any destroyed objects from the serializable list before saving.
        /// </param>
        public async Task SaveAsync(bool scanScene = false, bool clearDestroyed = false)
        {
            if (!hasLoaded)
            {
                Debug.LogError("[DataManager] SaveAsync() called before data was loaded or initialized — aborting to avoid overwriting the save file with blank data.");
                return;
            }

            if (scanScene)
            {
                FindAllSerializableObjects();
            }

            PrepareDataForSaving(clearDestroyed);
            await dataHandler.SaveAsync(currentData, usePlayerPrefs);
        }

        /// <summary>
        /// Calls <see cref="ISerializable.LoadData"/> on all registered serializable objects,
        /// applying the current <see cref="CurrentData"/> state to each.
        /// </summary>
        private void ApplyDataToObjects()
        {
            foreach (var saveable in serializableObjects)
            {
                saveable?.LoadData(currentData);
            }
        }

        /// <summary>
        /// Calls <see cref="ISerializable.SaveData"/> on all registered serializable objects,
        /// collecting their state into <see cref="CurrentData"/> before writing to disk.
        /// </summary>
        /// <param name="clearDestroyed">
        /// If true, purges destroyed objects from the list before collecting data.
        /// </param>
        private void PrepareDataForSaving(bool clearDestroyed)
        {
            if (clearDestroyed)
            {
                CleanupDestroyedObjects();
            }

            foreach (var serializable in serializableObjects)
            {
                serializable?.SaveData(currentData);
            }
        }

        /// <summary>
        /// Manually registers an <see cref="ISerializable"/> object with the manager.
        /// Use this for objects that are spawned at runtime and may not be found by
        /// <see cref="FindAllSerializableObjects"/>.
        /// </summary>
        public void AddSerializableObject(ISerializable serializable)
        {
            if (serializable != null)
            {
                serializableObjects.Add(serializable);
            }
        }

        /// <summary>
        /// Manually unregisters an <see cref="ISerializable"/> object from the manager.
        /// </summary>
        public void RemoveSerializableObject(ISerializable serializable)
        {
            if (serializable != null)
            {
                serializableObjects.Remove(serializable);
            }
        }

        /// <summary>
        /// Scans the scene for all <see cref="MonoBehaviour"/> instances that implement
        /// <see cref="ISerializable"/>, including inactive objects.
        /// Note: this class is not a MonoBehaviour, but still depends on Unity's scene
        /// via <see cref="Object.FindObjectsByType{T}"/>. Ensure it is only instantiated
        /// in a Unity context.
        /// </summary>
        private void FindAllSerializableObjects()
        {
            var serializables = Object.FindObjectsByType<MonoBehaviour>(
                            FindObjectsInactive.Include,
                            FindObjectsSortMode.InstanceID)
                    .OfType<ISerializable>()
                    .ToArray();

            serializableObjects.UnionWith(serializables);

            Debug.Log($"[DataManager] Found {serializables.Length} serializable objects.");
        }

        /// <summary>
        /// Removes null or destroyed entries from the serializable objects list.
        /// </summary>
        private void CleanupDestroyedObjects()
        {
            serializableObjects.RemoveWhere(obj => obj == null || obj is MonoBehaviour mb && mb == null);
        }
    }
}
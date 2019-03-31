using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using Jbta.VirtualFileSystem.Exceptions;
using Jbta.VirtualFileSystem.Impl;
using Jbta.VirtualFileSystem.Utils;

namespace Jbta.VirtualFileSystem.Mounting
{
    internal class Mounter
    {
        public async Task<IFileSystem> Mount(string volumePath)
        {
            if (!System.IO.File.Exists(volumePath)) throw new VolumeNotFoundException(volumePath);
            var blockSize = ValidateHeader(volumePath);
            
            var volume = new Volume(volumePath, blockSize);
            var superblock = await ReadSuperblock(volume);
            
            await MarkFileSystemAsDirty(volume, superblock);

            return FileSystemFactory.CreateFileSystem(volumePath, volume, superblock);
        }

        private static int ValidateHeader(string volumePath)
        {
            var buffer = new byte[sizeof(int) + sizeof(bool) + sizeof(int)];
            using (var stream = new FileStream(volumePath, FileMode.Open))
            {
                stream.Read(buffer);
            }

            var offset = 0;
            var magicNumber = BitConverter.ToInt32(buffer, offset);
            offset += sizeof(int) - 1;
            var isDirty = BitConverter.ToBoolean(buffer, offset);
            offset += sizeof(bool);
            var blockSize = BitConverter.ToInt32(buffer, offset);
            
            if (magicNumber != GlobalConstant.SuperblockMagicNumber)
            {
                throw new FileSystemException($"Invalid file system by volume path {volumePath}");
            }
            if (isDirty)
            {
                throw new FileSystemException("File system is already mounted or previous session was failed");
            }

            return blockSize;
        }
        
        private static async Task<Superblock> ReadSuperblock(Volume volume)
        {
            var superblockData = await volume.ReadBlocks(0, 1);
            using (var ms = new MemoryStream(superblockData))
                return (Superblock) new BinaryFormatter().Deserialize(ms);
        }

        private static ValueTask MarkFileSystemAsDirty(Volume volume, Superblock superblock)
        {
            superblock.IsDirty = !superblock.IsDirty;
            return volume.WriteBlocks(superblock.Serialize().ToArray(), new[] {0});
        }
    }
}
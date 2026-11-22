/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

namespace Aspire.Hosting;

/// <summary>The container image that <see cref="KahunaBuilderExtensions.AddKahuna"/> runs.</summary>
internal static class KahunaContainerImageTags
{
    /// <summary>docker.io</summary>
    public const string Registry = "docker.io";

    /// <summary>kahunakv/kahuna</summary>
    public const string Image = "kahunakv/kahuna";

    /// <summary>
    /// The pinned server version. Change it together with the Kahuna.Client version, so the client and
    /// the server in one AppHost come from the same release.
    /// </summary>
    public const string Tag = "1.17.3";
}

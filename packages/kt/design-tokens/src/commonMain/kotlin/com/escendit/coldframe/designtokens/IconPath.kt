package com.escendit.coldframe.designtokens

/** One absolute drawing command of an icon, in the 32×32 viewport. */
public sealed interface PathCommand {
    public data class MoveTo(
        val x: Float,
        val y: Float,
    ) : PathCommand

    public data class LineTo(
        val x: Float,
        val y: Float,
    ) : PathCommand

    public data class CubicTo(
        val x1: Float,
        val y1: Float,
        val x2: Float,
        val y2: Float,
        val x: Float,
        val y: Float,
    ) : PathCommand

    public data object Close : PathCommand
}

/** Parses [CarbonIcon.pathData]: space-separated `M x y`, `L x y`, `C x1 y1 x2 y2 x y` and `Z`. */
public object IconPath {
    /** Width and height of the viewport every path is drawn in. */
    public const val VIEWPORT_SIZE: Float = 32f

    /** @throws IllegalArgumentException when [data] is not normalised path data. */
    public fun parse(data: String): List<PathCommand> {
        val tokens = data.trim().split(' ').filter { it.isNotEmpty() }
        val commands = mutableListOf<PathCommand>()
        var index = 0

        fun numbers(count: Int): List<Float> {
            require(index + count <= tokens.size) { "Path data ends inside a command." }
            val values =
                tokens
                    .subList(
                        index,
                        index + count,
                    ).map { requireNotNull(it.toFloatOrNull()) { "Not a number: $it" } }
            index += count
            return values
        }

        while (index < tokens.size) {
            val command = tokens[index++]
            commands +=
                when (command) {
                    "M" -> numbers(2).let { PathCommand.MoveTo(it[0], it[1]) }
                    "L" -> numbers(2).let { PathCommand.LineTo(it[0], it[1]) }
                    "C" -> numbers(6).let { PathCommand.CubicTo(it[0], it[1], it[2], it[3], it[4], it[5]) }
                    "Z" -> PathCommand.Close
                    else -> throw IllegalArgumentException("Unsupported path command: $command")
                }
        }
        return commands
    }
}

﻿Imports System.IO
Imports System.Runtime.CompilerServices

Public Delegate Sub ProgressUpdatedDelegate(progress As Single)
Public Delegate Sub MessageOutDelegate(message As String, textColor As ConsoleColor)

Public Module BinVote
    Public Const BitReconstructorPrefix = "BitReconstructor."
    Public Const StreamsCountMin As Integer = 3
    Public Const StreamBufferSize As Integer = 128 * 1024 * 1024
    Public Const InternalBufferSize As Integer = 8 * 1024 * 1024

    Public Class BinVoteTask
        Public Property InputStreams As Stream()
        Public Property OutputFilename As String
        Public Property OutputStream As Stream
        Public ReadOnly Property InputsCount As Integer
            Get                
                Return If(InputStreams IsNot Nothing, InputStreams.Length, -1)
            End Get
        End Property
        Public ReadOnly Property OutputSize As Long
            Get
                Return If(InputStreams IsNot Nothing, InputStreams(0).Length, -1)
            End Get
        End Property
        Public Sub New()
        End Sub
        Public Sub New(outputFilename As String, inputStreams As Stream())
            Me.OutputFilename = outputFilename : Me.InputStreams = inputStreams
        End Sub
        Public Sub Close(messageOutHandler As MessageOutDelegate)
            Try
                If OutputStream IsNot Nothing Then
                    OutputStream.Flush() : OutputStream.Close() : OutputStream = Nothing
                End If
                For Each stream In InputStreams
                    If stream IsNot Nothing Then stream.Close() : stream = Nothing
                Next
                InputStreams = Nothing
            Catch ex As Exception
                If messageOutHandler IsNot Nothing Then
                    messageOutHandler.Invoke(String.Empty, ConsoleColor.Gray) : messageOutHandler.Invoke(String.Format(ex.ToString()), ConsoleColor.Red)
                End If
            End Try
        End Sub
    End Class

    Public Function GetMaxDamageCount(inputsCount As Integer) As Integer
        If inputsCount Mod 2 = 0 Then inputsCount -= 1
        If inputsCount < StreamsCountMin Then Return 0 Else Return Math.Floor(inputsCount / 2.0)
    End Function

    Public Function ShortTest(inputsCount As Integer, messageOutHandler As MessageOutDelegate) As Boolean
        If inputsCount < StreamsCountMin Then inputsCount = StreamsCountMin
        Dim allOk As Boolean
        Dim result = BinVoteTest.ShortTest(inputsCount, allOk)
        If messageOutHandler IsNot Nothing Then
            messageOutHandler.Invoke(String.Format("Inputs: {0,6}", inputsCount), ConsoleColor.Green)
            messageOutHandler.Invoke(String.Format("Performance: {0:0.00} Mb/s", result / (1024 * 1024)), ConsoleColor.Green)
            messageOutHandler.Invoke(String.Empty, ConsoleColor.Gray)
        End If
        Return allOk
    End Function

    Public Function CheckUp(args As String(), ByRef inputsCount As Integer, ByRef outputSize As Long, ByRef outputFilename As String, messageOutHandler As MessageOutDelegate) As Boolean
        Dim task = BinVote.GetBinVoteTask(BitReconstructorPrefix, args, False, False, Nothing, Nothing)
        If task IsNot Nothing Then
            inputsCount = task.InputsCount : outputSize = task.OutputSize : outputFilename = task.OutputFilename : task.Close(messageOutHandler)
            Return True
        Else
            inputsCount = 0 : outputSize = 0 : outputFilename = String.Empty
            Return False
        End If
    End Function

    Public Function Process(args As String(), outputNameSpecified As Boolean, createOutput As Boolean, messageOutHandler As MessageOutDelegate, progressUpdatedHandler As ProgressUpdatedDelegate) As Boolean
        If ShortTest(args.Length, messageOutHandler) Then
            If messageOutHandler IsNot Nothing Then messageOutHandler.Invoke("Self-test: OK", ConsoleColor.Gray)
        Else
            If messageOutHandler IsNot Nothing Then messageOutHandler.Invoke("Self-test: Failed", ConsoleColor.Red)
            Return False
        End If
        Dim task = BinVote.GetBinVoteTask(BitReconstructorPrefix, args, outputNameSpecified, createOutput, messageOutHandler, progressUpdatedHandler)
        If task Is Nothing Then
            If messageOutHandler IsNot Nothing Then
                messageOutHandler.Invoke("Nothing to do!", ConsoleColor.Yellow)
                If outputNameSpecified Then
                    messageOutHandler.Invoke("Please, pass at least 3 input files as arguments and 4th as output!", ConsoleColor.Yellow)
                Else
                    messageOutHandler.Invoke("Please, pass at least 3 input files as arguments!", ConsoleColor.Yellow)
                End If
            End If
        Else
            ExecuteBinVoteTask(task, messageOutHandler, progressUpdatedHandler)
        End If
        Return True
    End Function

    Public Function Process(inputs As Stream(), output As Stream, messageOutHandler As MessageOutDelegate, progressUpdatedHandler As ProgressUpdatedDelegate, Optional ByVal streamBufferSize As Integer = InternalBufferSize) As Boolean
        Dim weights As Integer() = New Integer(inputs.Length - 1) {}
        For i = 0 To weights.Length - 1
            weights(i) = 1
        Next
        Return Process(inputs, weights, output, messageOutHandler, progressUpdatedHandler, streamBufferSize)
    End Function

    Public Function Process(inputs As Stream(), weights As Integer(), output As Stream, messageOutHandler As MessageOutDelegate, progressUpdatedHandler As ProgressUpdatedDelegate, Optional ByVal streamBufferSize As Integer = InternalBufferSize) As Boolean
        If inputs.Length <> weights.Length Then
            Throw New Exception("inputs.Length <> weights.Length")
        End If
        Dim outputSize As Long = 0 : Dim streamsCount As Integer = 0 : DominLengthStreamFilter(inputs, weights, outputSize, streamsCount)
        If streamsCount < StreamsCountMin Then
            If messageOutHandler IsNot Nothing Then messageOutHandler.Invoke(String.Format("The number of input streams after filtering by size is {0}, the binary vote is impossible!", streamsCount), ConsoleColor.Red)
            Return False
        Else
            If messageOutHandler IsNot Nothing Then messageOutHandler.Invoke(String.Format("Voting: {0,4} streams ({1} bytes each)", streamsCount, inputs(0).Length), ConsoleColor.Gray)
        End If
        Dim streamLength = inputs(0).Length
        Dim fullBufferIters = CLng(Math.Floor(streamLength / streamBufferSize))
        Dim inputBuffers As Byte()() = New Byte(inputs.Length - 1)() {}
        Dim outputBuffer As Byte()
        If fullBufferIters <> 0 Then
            For i = 0 To inputs.Length - 1
                inputBuffers(i) = New Byte(streamBufferSize - 1) {}
            Next
            outputBuffer = New Byte(streamBufferSize - 1) {}
            For i = 0 To fullBufferIters - 1
                FillInputBuffers(inputBuffers, inputs) : Process(inputBuffers, weights, outputBuffer) : output.Write(outputBuffer, 0, outputBuffer.Length)
                If progressUpdatedHandler IsNot Nothing Then progressUpdatedHandler.Invoke((i + 1) / CSng(fullBufferIters + 1))
            Next
        End If
        Dim remainBytes = streamLength - (streamBufferSize * fullBufferIters)
        If remainBytes <> 0 Then
            For i = 0 To inputs.Length - 1
                inputBuffers(i) = New Byte(remainBytes - 1) {}
            Next
            outputBuffer = New Byte(remainBytes - 1) {}
            FillInputBuffers(inputBuffers, inputs) : Process(inputBuffers, weights, outputBuffer) : output.Write(outputBuffer, 0, outputBuffer.Length)
        End If
        If progressUpdatedHandler IsNot Nothing Then progressUpdatedHandler.Invoke(1.0F)
        output.Flush() : Return True
    End Function

    Private Sub DominLengthStreamFilter(ByRef inputs As Stream(), ByRef weights As Integer(), ByRef outputSize As Long, ByRef outputCount As Integer)
        Dim equals As Integer() = New Integer(inputs.Length - 1) {}
        For i = 0 To inputs.Length - 1
            For j = 0 To inputs.Length - 1
                If inputs(i).Length = inputs(j).Length Then equals(i) += weights(i)
            Next
        Next
        Dim maxEqualsVal = equals(0)
        Dim maxEqualsLength = inputs(0).Length
        Dim maxEqualsIdx As Integer = 0
        For i = 1 To equals.Length - 1
            If equals(i) >= maxEqualsVal AndAlso inputs(i).Length > maxEqualsLength Then
                maxEqualsVal = equals(i) : maxEqualsLength = inputs(i).Length : maxEqualsIdx = i
            End If
        Next
        outputSize = inputs(maxEqualsIdx).Length : outputCount = 0
        For i = 0 To inputs.Length - 1
            If inputs(i).Length = outputSize Then
                outputCount += 1
                If inputs(i).CanSeek Then inputs(i).Seek(0, SeekOrigin.Begin)
            Else
                inputs(i).Close() : inputs(i) = Nothing
            End If
        Next
        Dim inputsFilteredList As New List(Of Stream)
        Dim weightsFilteredList As New List(Of Integer)
        For i = 0 To inputs.Length - 1
            If inputs(i) IsNot Nothing Then
                inputsFilteredList.Add(inputs(i)) : weightsFilteredList.Add(weights(i))
            End If
        Next
        Array.Resize(inputs, inputsFilteredList.Count) : Array.Resize(weights, weightsFilteredList.Count)
        Array.Copy(inputsFilteredList.ToArray(), inputs, inputs.Length) : Array.Copy(weightsFilteredList.ToArray(), weights, weights.Length)
    End Sub

    Private Function GetBinVoteTask(prefix As String, args As String(), outputNameSpecified As Boolean, createOutput As Boolean, messageOutHandler As MessageOutDelegate, progressUpdatedHandler As ProgressUpdatedDelegate) As BinVoteTask
        Dim binVoteStreamsCountMin = If(outputNameSpecified, BinVote.StreamsCountMin + 1, BinVote.StreamsCountMin)
        If args.Length >= binVoteStreamsCountMin Then
            Dim task = New BinVoteTask() With {.OutputFilename = If(outputNameSpecified, args(args.Length - 1), Path.Combine(Path.GetDirectoryName(args(0)), prefix + Path.GetFileName(args(0))))}
            Dim streams = New List(Of Stream)
            Dim argsList = New LinkedList(Of String)(args) : If outputNameSpecified Then argsList.RemoveLast()
            Try
                For Each arg In argsList
                    If File.Exists(arg) Then streams.Add(New BufferedStream(File.Open(arg, FileMode.Open), StreamBufferSize))
                Next
                task.InputStreams = streams.ToArray()
                Dim weights As Integer() = New Integer(task.InputStreams.Length - 1) {}
                For i = 0 To weights.Length - 1
                    weights(i) = 1
                Next
                Dim outputSize As Long = 0 : Dim streamsCount As Integer = 0 : DominLengthStreamFilter(task.InputStreams, weights, task.OutputSize, streamsCount)
                If createOutput Then
                    If File.Exists(task.OutputFilename) Then
                        File.SetAttributes(task.OutputFilename, FileAttributes.Normal) : File.Delete(task.OutputFilename)
                    End If
                    task.OutputStream = New BufferedStream(File.Open(task.OutputFilename, FileMode.CreateNew), StreamBufferSize)
                End If
            Catch ex As Exception
                If messageOutHandler IsNot Nothing Then
                    messageOutHandler.Invoke(String.Empty, ConsoleColor.Gray) : messageOutHandler.Invoke(String.Format(ex.ToString()), ConsoleColor.Red) : Return Nothing
                End If
            End Try
            Return task
        End If
        Return Nothing
    End Function

    Private Sub ExecuteBinVoteTask(task As BinVoteTask, messageOutHandler As MessageOutDelegate, progressUpdatedHandler As ProgressUpdatedDelegate)
        Try
            If messageOutHandler IsNot Nothing Then messageOutHandler.Invoke(String.Format("Output:    {0}", task.OutputFilename), ConsoleColor.Gray)
            If BinVote.Process(task.InputStreams, task.OutputStream, messageOutHandler, progressUpdatedHandler) Then messageOutHandler.Invoke(String.Empty, ConsoleColor.Gray)
            task.Close(messageOutHandler)
        Catch ex As Exception
            If messageOutHandler IsNot Nothing Then
                messageOutHandler.Invoke(String.Empty, ConsoleColor.Gray) : messageOutHandler.Invoke(String.Format(ex.ToString()), ConsoleColor.Red)
            End If
        End Try
    End Sub

    Private Sub FillInputBuffers(inputBuffers As Byte()(), inputs As Stream())
        Dim rowsCount = inputBuffers(0).Length
        Parallel.For(0, inputs.Length, Sub(i As Integer)
                                           Dim done As Integer = 0
                                           Dim task As Integer = rowsCount
                                           While task > 0
                                               done += inputs(i).Read(inputBuffers(i), done, task) : task = rowsCount - done
                                           End While
                                       End Sub)
    End Sub

    Private Sub Process(inputBuffers As Byte()(), weights As Integer(), output As Byte())
        Dim rowsCount = inputBuffers(0).Length
        Parallel.For(0, rowsCount, Sub(row As Integer)
                                       Dim slice As Byte() = New Byte(inputBuffers.Length - 1) {}
                                       For i = 0 To inputBuffers.Length - 1
                                           slice(i) = inputBuffers(i)(row)
                                       Next
                                       output(row) = Vote(slice, weights)
                                   End Sub)
        Return
    End Sub

    Private Function Vote(slice As Byte(), weights As Integer()) As Byte
        Dim c0, c1, c2, c3, c4, c5, c6, c7 As Integer
        Dim b0, b1, b2, b3, b4, b5, b6, b7 As Integer
        b0 = 1 : b1 = 2 : b2 = 4 : b3 = 8 : b4 = 16 : b5 = 32 : b6 = 64 : b7 = 128
        For i = 0 To slice.Length - 1
            Dim s = slice(i) : Dim w = weights(i)
            If (s And b0) <> 0 Then c0 += w Else c0 -= w
            If (s And b1) <> 0 Then c1 += w Else c1 -= w
            If (s And b2) <> 0 Then c2 += w Else c2 -= w
            If (s And b3) <> 0 Then c3 += w Else c3 -= w
            If (s And b4) <> 0 Then c4 += w Else c4 -= w
            If (s And b5) <> 0 Then c5 += w Else c5 -= w
            If (s And b6) <> 0 Then c6 += w Else c6 -= w
            If (s And b7) <> 0 Then c7 += w Else c7 -= w
        Next
        If c0 < 0 Then b0 = 0
        If c1 < 0 Then b1 = 0
        If c2 < 0 Then b2 = 0
        If c3 < 0 Then b3 = 0
        If c4 < 0 Then b4 = 0
        If c5 < 0 Then b5 = 0
        If c6 < 0 Then b6 = 0
        If c7 < 0 Then b7 = 0
        Return b0 Or b1 Or b2 Or b3 Or b4 Or b5 Or b6 Or b7
    End Function
End Module
